# Overview
Find duplicates in your file system and save some space! 🧹

![Screenshot](screenshot1.png "Duplicate Detector UI")

# Usage
- After launching the program, you'll start off with a blank comparison.
- To begin looking for duplicates, either right-click on the blank white area and select `Add Folders...` or simply drag a folder onto the window.
- The programm will immediately start scanning and calculate a "fingerprint" (hash) for every single file. If two or more files have the same fingerprint, it will be shown as a possible duplicate.
  - **Note**: Currently, SHA-1 is being used, mainly for performance reasons
  - **Note**: Having the same fingerprint is very likely indicating files with equal contents; however, SHA-1 does have a possible attack that allows attackers to prepare a file that looks as if it has the same contents as another file, but in fact does not
- To add more folders to your existing session, hold the `CTRL` key while dragging folders onto the window
- To start a new session, right click on the main area of the window and select `Clear`

# Building from source

## Prerequisites
- [Microsoft .NET 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (Most likely, you'll need the SDK Installer for Windows x64)

## 

To download the sources and build, run the following lines from the PowerShell or similar:
```
git clone https://github.com/fhuber83/DuplicateDetector
cd DuplicateDetector
dotnet build --configuration Release
```

The binary will be placed under `bin\Release\net8.0-windows\`, relative to the folder you've just cloned into. To test if the program works, run the following line from the same Shell you just used:

```
.\bin\Release\net8.0-windows\DuplicateDetector.exe
```
