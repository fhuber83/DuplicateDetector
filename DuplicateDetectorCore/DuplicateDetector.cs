using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        public static void GetFileNamesRecursively(string rootPath, List<string> list, CancellationToken token, UpdateStatus status)
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

                        if (list.Count % 100 == 0)
                        {
                            status?.Invoke(ProcessingStage.Enumerating, null, list.Count);
                        }
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
                //using (SHA1 sha1 = SHA1.Create())
                {
                    //var bytes = File.ReadAllBytes(path);
                    //sha1.ComputeHash(bytes);
                    //bytes = null;

                    //if (sha1.Hash is not null)
                    {
                        //var sb = new StringBuilder();

                        //foreach (var b in sha1.Hash)
                        //{
                        //    sb.Append($"{b:X2}");
                        //}
                        var hashString = CalculateHash(path); // sb.ToString();

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
            }
            catch (Exception)
            {
            }
        }

        public DuplicateDetector()
        {

        }

        /// <summary>
        /// For each hash, this contains a list of files that have that hash
        /// </summary>
        public Dictionary<String, HashSummaryItem>? HashMap = null;


        public enum ProcessingStage
        {
            Enumerating,
            Processing,
            PostProcessing,
            Done,
            Cancelled,
        }

        public delegate void UpdateStatus(ProcessingStage stage, double? percentage, int? total);

        public long TotalSize { get; private set; } = 0L;

        public async Task ProcessDirectoryAsync(string[] files, bool keepSingleFiles, CancellationToken cancellationToken, UpdateStatus status = null)
        {
            var result = await Task.Run(() =>
            {
                var fileInfos = new List<DuplicateFileInfo>();
                var hashMap = new Dictionary<string, HashSummaryItem>();
                
                // Create a list of all files to check

                if(status != null)
                {
                    status(ProcessingStage.Enumerating, null, 0);
                }

                var listOfFiles = new List<string>();

                foreach (var file in files)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        if(status != null)
                        {
                            status(ProcessingStage.Cancelled, 0.0, null);
                        }
                        return (false, null, null, 0L);
                    }

                    if (Directory.Exists(file))
                    {
                        GetFileNamesRecursively(file, listOfFiles, cancellationToken, status);
                    }
                    else if (File.Exists(file))
                    {
                        listOfFiles.Add(file);
                    }

                    if (status != null)
                    {
                        status(ProcessingStage.Enumerating, null, listOfFiles.Count);
                    }
                }

                //long totalSize = 0L;
                //foreach(var file in listOfFiles)
                //{
                //    try
                //    {
                //        totalSize += new FileInfo(file).Length;
                //    }
                //    catch { }   
                //}

                int maxCount = listOfFiles.Count;

                int count = 0;

                // Calculate hashes in parallel (Processing stage)
                var infoBag = new ConcurrentBag<DuplicateFileInfo>();

                //long bytesDone = 0L;

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
                        //Interlocked.Add(ref bytesDone, new FileInfo(file).Length);

                        if (status != null)
                        {
                            var percentage = (count * 100.0) / maxCount;
                            //var percentage = (bytesDone * 100.0) / totalSize;
                            
                            status(ProcessingStage.Processing, percentage, null);
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
                    status(ProcessingStage.PostProcessing, 0.0, null);
                }


                // Post-processing stage
                int num = infoBag.Count;
                int done = 0;
                long totalSize = 0L;

                // infoBag contains one entry for each file
                foreach (var info in infoBag)
                {
                    if(status != null)
                    {
                        var percentage = (done * 100.0) / num;
                        status(ProcessingStage.PostProcessing, 0.5 * percentage, null);
                    }

                    // Copy item from bag to List
                    fileInfos.Add(info);

                    //totalSize += info.FileSize;

                    // Hash already in map?
                    if (!hashMap.ContainsKey(info.Hash))
                    {
                        // No, create new entry for this hash
                        hashMap.Add(info.Hash, new HashSummaryItem());
                    }

                    // Add this item to the hash map
                    hashMap[info.Hash].Files.Add(info);

                    done += 1;
                } // for each item in infoBag

                done = 0;
                num = hashMap.Keys.Count;

                Parallel.ForEach(hashMap.Keys, parallelOptions, hash =>
                {
                    var progress = (100.0 * done) / num;
                    status?.Invoke(ProcessingStage.PostProcessing, 50.0 + 0.5 * progress, 0);

                    var numFilesWithThisHash = hashMap[hash].Files.Count;

                    // Set each item's "number of duplicate" counter
                    foreach (var item in hashMap[hash].Files)
                    {
                        item.Count = numFilesWithThisHash;
                    }

                    // Update filenames string
                    var distinctNames = hashMap[hash].Files.Select(x => x.FileName).Distinct().ToList();

                    var sb = new StringBuilder();

                    for (int i = 0; i < distinctNames.Count; i++)
                    {
                        sb.Append(distinctNames[i]);

                        if (i < (distinctNames.Count - 1))
                        {
                            sb.Append(", ");
                        }
                    }

                    hashMap[hash].FileNames = sb.ToString();


                    // Calculate the total number of bytes used for each hash
                    long total = 0;

                    var files = hashMap[hash].Files;

                    foreach (var item in files)
                    {
                        total += item.FileSize;
                    }

                    hashMap[hash].TotalSize = total;

                    Interlocked.Add(ref totalSize, hashMap[hash].TotalSize);
                    Interlocked.Increment(ref done);
                });

                TotalSize = totalSize;

                return (true, fileInfos, hashMap, TotalSize);
            });


            // If operation completed successfully
            if (result.Item1)
            {
                HashMap = result.hashMap;

                foreach (var hash in HashMap.Keys.ToList())
                {
                    if (HashMap[hash].Files.Count == 1 && !keepSingleFiles)
                    {
                        HashMap.Remove(hash);
                    }
                }

                if(status != null)
                {
                    status(ProcessingStage.Done, 100.0, null);
                }
            }
        }



        public static string CalculateHash(string path)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                var fileInfo = new System.IO.FileInfo(path);

                //if(fileInfo.Length < (2L * 1024L * 1024L * 1024L))
                //{ 
                //    sha1.ComputeHash(File.ReadAllBytes(path));
                //}
                //else
                {
                    using (var fileStream = File.OpenRead(path))
                    {
                        sha1.ComputeHash(fileStream);
                    }
                }

                if (sha1.Hash is null)
                    throw new Exception("Unable to calculate hash");

                var sb = new StringBuilder();

                foreach (var b in sha1.Hash)
                {
                    sb.Append($"{b:X2}");
                }

                sha1.Clear();

                return sb.ToString();
            }
        }

        public void MergeWith(DuplicateDetector other)
        {
            foreach(var hash in other.HashMap.Keys)
            {
                // Hash already exists, update existing items
                if (HashMap.ContainsKey(hash))
                {
                    foreach(var otherFile in other.HashMap[hash].Files)
                    {
                        // Only copy files that are not already in the list
                        if(!HashMap[hash].Files.Any(x => x.Path == otherFile.Path))
                        {
                            HashMap[hash].Files.Add(otherFile);
                            HashMap[hash].TotalSize += otherFile.FileSize;
                        }
                        else
                        {
                            Debug.Print("Ignoring file {0}", otherFile.FileName);
                        }
                    }

                    // Update list of distinct file names
                    var distinctNames = HashMap[hash].Files.Select(x => x.FileName).Distinct().ToList();

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

                        HashMap[hash].FileNames = sb.ToString();
                    }

                }

                // New hash, add duplicate files from other
                else
                {
                    if (other.HashMap[hash].Files.Count > 1)
                    {
                        HashMap.Add(hash, other.HashMap[hash]);
                    }
                }
            }
        }
    }
}
