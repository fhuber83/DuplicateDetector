﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DuplicateDetectorCore;
using Microsoft.VisualBasic;

namespace DuplicateDetectorCore
{
    public class DuplicateDetector
    {
        public static void GetFileNamesRecursively(string rootPath, List<string> list, CancellationToken token)
        {
            if (!Directory.Exists(rootPath))
                throw new ArgumentException("Must be an existing directory", nameof(rootPath));

            var directoriesToDo = new Stack<string>();

            directoriesToDo.Push(rootPath);

            while (directoriesToDo.Count > 0)
            {
                if (token.IsCancellationRequested)
                    break;

                var path = directoriesToDo.Pop();

                try
                {
                    foreach (var subDirectory in Directory.GetDirectories(path))
                    {
                        if (token.IsCancellationRequested)
                            break;

                        directoriesToDo.Push(subDirectory);
                    }

                    foreach (var file in Directory.GetFiles(path))
                    {
                        if (token.IsCancellationRequested)
                            break;

                        list.Add(file);
                    }
                }
                catch (Exception)
                {
                    // Ignore, probably unauthorized to access this folder
                }
            }
        }

        public static void ProcessFile(string path, ConcurrentBag<DuplicateFileInfo> list)
        {
            try
            {
                SHA1 sha1 = SHA1.Create();

                sha1.ComputeHash(File.ReadAllBytes(path));

                if (sha1.Hash is not null)
                {
                    var hashString = CalculateHash(path);

                    var fileInfo = new FileInfo(path);

                    var item = new DuplicateFileInfo
                    {
                        FileName = System.IO.Path.GetFileName(path),
                        Path = System.IO.Path.GetDirectoryName(path),
                        Hash = hashString,
                        FileSize = fileInfo.Length,
                        LastChange = fileInfo.LastWriteTime,
                        CreationTime = fileInfo.CreationTime
                    };

                    list.Add(item);
                }
            }
            catch (Exception)
            {
            }
        }

        public DuplicateDetector()
        {

        }

        public Dictionary<String, HashSummaryItem>? HashMap = null;

        public List<DuplicateFileInfo>? FileItems = null;

        public enum ProcessingStage
        {
            Enumerating,
            Processing,
            PostProcessing,
            Done,
        }

        public delegate void UpdateStatus(ProcessingStage stage, double percentage);

        public async Task ProcessDirectoryAsync(string[] files, CancellationToken cancellationToken, UpdateStatus status)
        {
            var result = await Task.Run(() =>
            {
                var fileInfos = new List<DuplicateFileInfo>();
                var hashMap = new Dictionary<string, HashSummaryItem>();
                long totalSize = 0L;

                // Create a list of all files to check
                var listOfFiles = new List<string>();

                foreach (var file in files)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return (false, null, null, 0L);
                    }

                    if (Directory.Exists(file))
                    {
                        GetFileNamesRecursively(file, listOfFiles, cancellationToken);
                    }
                    else if (File.Exists(file))
                    {
                        listOfFiles.Add(file);
                    }
                }

                int maxCount = listOfFiles.Count;

                int count = 0;

                // Calculate hashes in parallel
                var infoBag = new ConcurrentBag<DuplicateFileInfo>();

                ParallelOptions parallelOptions = new()
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                try
                {
                    Parallel.ForEach(listOfFiles, parallelOptions, file =>
                    {
                        ProcessFile(file, infoBag);

                        Interlocked.Increment(ref count);

                        if(status != null)
                        {
                            var percentage = (count * 100.0) / maxCount;
                            status(ProcessingStage.Processing, percentage);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    return (false, null, null, 0L);
                }

                // Build duplicate dictionary
                if(status != null)
                {
                    status(ProcessingStage.PostProcessing, 0.0);
                }


                foreach (var info in infoBag)
                {
                    // Copy item from bag to List
                    fileInfos.Add(info);

                    totalSize += info.FileSize;

                    // Hash already in map?
                    if (!hashMap.ContainsKey(info.Hash))
                    {
                        // No, create new entry for this hash
                        hashMap.Add(info.Hash, new HashSummaryItem());
                    }

                    // Add this item to the hash map
                    hashMap[info.Hash].Files.Add(info);

                    // Set each item's "number of duplicate" counter
                    foreach (var item in hashMap[info.Hash].Files)
                    {
                        item.Count = hashMap[info.Hash].Files.Count;
                    }

                    var distinctNames = hashMap[info.Hash].Files.Select(x => x.FileName).Distinct().ToList();

                    {
                        var sb = new StringBuilder();

                        for (int i = 0; i < distinctNames.Count; i++)
                        {
                            sb.Append(distinctNames[i]);

                            if (i < (distinctNames.Count - 1))
                            {
                                sb.Append(", ");
                            }
                        }

                        hashMap[info.Hash].FileNames = sb.ToString();
                    }

                    // Calculate the total number of bytes used for each hash
                    try
                    {
                        Parallel.ForEach(hashMap.Keys, parallelOptions, hash =>
                        {
                            long total = 0;

                            foreach (var item in hashMap[hash].Files)
                            {
                                total += item.FileSize;
                            }

                            hashMap[hash].TotalSize = total;
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        return (false, null, null, 0L);
                    }
                }

                return (true, fileInfos, hashMap, totalSize);
            });


            if (result.Item1)
            {
                HashMap = result.hashMap;

                foreach (var hash in HashMap.Keys.ToList())
                {
                    if (HashMap[hash].Files.Count == 1)
                    {
                        HashMap.Remove(hash);
                    }
                }

                if(status != null)
                {
                    status(ProcessingStage.Done, 100.0);
                }
            }
        }


        const long blockSize = 1024L * 1024L * 256L; // Number of bytes to process at a time

        public static string CalculateHash(string path)
        {
            SHA1 sha1 = SHA1.Create();

            var fileInfo = new System.IO.FileInfo(path);

            // Process entire file at once
            if (fileInfo.Length <= blockSize)
            {
                sha1.ComputeHash(File.ReadAllBytes(path));
            }
            else
            {
                var buffer = new byte[blockSize];

                var fileStream = File.OpenRead(path);

                long sizeRemaining = fileInfo.Length;

                while (sizeRemaining > 0L)
                {
                    long sizeToRead = sizeRemaining < blockSize ? sizeRemaining : blockSize;

                    int bytesRead = fileStream.Read(buffer, 0, (int)sizeToRead);

                    sha1.ComputeHash(buffer, 0, bytesRead);

                    sizeRemaining -= sizeToRead;
                }
            }

            if (sha1.Hash is null)
                throw new Exception("Unable to calculate hash");

            var sb = new StringBuilder();

            foreach (var b in sha1.Hash)
            {
                sb.Append($"{b:X2}");
            }

            return sb.ToString();
        }
    }
}