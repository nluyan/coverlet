﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Coverlet.Core.Instrumentation
{
    /// <summary>
    /// This static class will be injected on a module being instrumented in order to direct on module hits
    /// to a single location.
    /// </summary>
    /// <remarks>
    /// As this type is going to be customized for each instrumented module it doesn't follow typical practices
    /// regarding visibility of members, etc.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    public static class ModuleTrackerTemplate
    {
        public static string HitsFilePath;
        public static int[] TimeArray; //clapp add
        public static int[] HitsArray;
        public static bool SingleHit;
        public static byte[] HitsData, TimeData; //clapp add
        private static readonly bool _enableLog = false;// int.TryParse(Environment.GetEnvironmentVariable("COVERLET_ENABLETRACKERLOG"), out int result) ? result == 1 : false;

        //clapp add
        static Stopwatch sw = new Stopwatch();
        //clapp add
        public static StepOverHandler StepOver;

        static ModuleTrackerTemplate()
        {
            // At the end of the instrumentation of a module, the instrumenter needs to add code here
            // to initialize the static fields according to the values derived from the instrumentation of
            // the module.
        }

        // A call to this method will be injected in the static constructor above for most cases. However, if the
        // current assembly is System.Private.CoreLib (or more specifically, defines System.AppDomain), a call directly
        // to UnloadModule will be injected in System.AppContext.OnProcessExit.
        public static void RegisterUnloadEvents()
        {
            AppDomain.CurrentDomain.ProcessExit += new EventHandler(UnloadModule);
            AppDomain.CurrentDomain.DomainUnload += new EventHandler(UnloadModule);
        }

        public static void RecordHitInCoreLibrary(int hitLocationIndex)
        {
            // Make sure to avoid recording if this is a call to RecordHit within the AppDomain setup code in an
            // instrumented build of System.Private.CoreLib.
            if (HitsArray is null)
                return;

            Interlocked.Increment(ref HitsArray[hitLocationIndex]);
        }

        public static void RecordHit(int hitLocationIndex)
        {
            if (!sw.IsRunning)
                sw.Start();
            if (TimeArray == null)
                TimeArray = new int[HitsArray.Length];
            sw.Stop();
            Interlocked.Add(ref TimeArray[hitLocationIndex], (int)sw.ElapsedMilliseconds);
            StepOver?.Invoke(hitLocationIndex);
            Interlocked.Increment(ref HitsArray[hitLocationIndex]);
            sw.Restart();
        }

        //clapp add
        public static void SaveLocalVariable(string name, object value)
        {
            locals[name] = value;
        }

        public static Dictionary<string, object> locals = new Dictionary<string, object>();

        public static void RecordSingleHitInCoreLibrary(int hitLocationIndex)
        {
            // Make sure to avoid recording if this is a call to RecordHit within the AppDomain setup code in an
            // instrumented build of System.Private.CoreLib.
            if (HitsArray is null)
                return;

            ref int location = ref HitsArray[hitLocationIndex];
            if (location == 0)
                location = 1;
        }

        public static void RecordSingleHit(int hitLocationIndex)
        {
            if (TimeArray == null)
                TimeArray = new int[HitsArray.Length];

            ref int location = ref HitsArray[hitLocationIndex];
            if (location == 0)
                location = 1;
        }

        public static void UnloadModule(object sender, EventArgs e)
        {
            sw.Stop();
            try
            {
                //WriteLog($"Unload called for '{Assembly.GetExecutingAssembly().Location}'");
                // Claim the current hits array and reset it to prevent double-counting scenarios.
                int[] hitsArray = Interlocked.Exchange(ref HitsArray, new int[HitsArray.Length]);

                //clapp add
                using (var fs = new MemoryStream())
                {
                    using (var bw = new BinaryWriter(fs))
                    {
                        bw.Write(hitsArray.Length);
                        foreach (int hitCount in hitsArray)
                        {
                            bw.Write(hitCount);
                        }
                    }
                    HitsData = fs.ToArray();
                }

                if (TimeArray != null)
                {
                    var timeArray = Interlocked.Exchange(ref TimeArray, new int[TimeArray.Length]);
                    using (var fs = new MemoryStream())
                    {
                        using (var bw = new BinaryWriter(fs))
                        {
                            bw.Write(timeArray.Length);
                            foreach (int time in timeArray)
                            {
                                bw.Write(time);
                            }
                        }
                        TimeData = fs.ToArray();
                    }
                }
                //clapp add end

                // The same module can be unloaded multiple times in the same process via different app domains.
                // Use a global mutex to ensure no concurrent access.
                //using (var mutex = new Mutex(true, Path.GetFileNameWithoutExtension(HitsFilePath) + "_Mutex", out bool createdNew))
                //{
                //    WriteLog($"Flushing hit file '{HitsFilePath}'");
                //    if (!createdNew)
                //        mutex.WaitOne();

                //    bool failedToCreateNewHitsFile = false;
                //    try
                //    {
                //        using (var fs = new FileStream(HitsFilePath, FileMode.CreateNew))
                //        using (var bw = new BinaryWriter(fs))
                //        {
                //            bw.Write(hitsArray.Length);
                //            foreach (int hitCount in hitsArray)
                //            {
                //                bw.Write(hitCount);
                //            }
                //        }
                //    }
                //    catch (Exception ex)
                //    {
                //        WriteLog($"Failed to create new hits file '{HitsFilePath}'\n{ex}");
                //        failedToCreateNewHitsFile = true;
                //    }

                //    if (failedToCreateNewHitsFile)
                //    {
                //        // Update the number of hits by adding value on disk with the ones on memory.
                //        // This path should be triggered only in the case of multiple AppDomain unloads.
                //        using (var fs = new FileStream(HitsFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                //        using (var br = new BinaryReader(fs))
                //        using (var bw = new BinaryWriter(fs))
                //        {
                //            int hitsLength = br.ReadInt32();
                //            WriteLog($"Current hits found '{hitsLength}'");

                //            if (hitsLength != hitsArray.Length)
                //            {
                //                throw new InvalidOperationException(
                //                    $"{HitsFilePath} has {hitsLength} entries but on memory {nameof(HitsArray)} has {hitsArray.Length}");
                //            }

                //            for (int i = 0; i < hitsLength; ++i)
                //            {
                //                int oldHitCount = br.ReadInt32();
                //                bw.Seek(-sizeof(int), SeekOrigin.Current);
                //                if (SingleHit)
                //                    bw.Write(hitsArray[i] + oldHitCount > 0 ? 1 : 0);
                //                else
                //                    bw.Write(hitsArray[i] + oldHitCount);
                //            }
                //        }
                //    }

                //    WriteHits();

                //    // On purpose this is not under a try-finally: it is better to have an exception if there was any error writing the hits file
                //    // this case is relevant when instrumenting corelib since multiple processes can be running against the same instrumented dll.
                //    mutex.ReleaseMutex();
                //    WriteLog($"Hit file '{HitsFilePath}' flushed, size {new FileInfo(HitsFilePath).Length}");
                //}
            }
            catch (Exception ex)
            {
                WriteLog(ex.ToString());
                throw;
            }
        }

        private static void WriteHits()
        {
            if (_enableLog)
            {
                Assembly currentAssembly = Assembly.GetExecutingAssembly();
                DirectoryInfo location = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(currentAssembly.Location), "TrackersHitsLog"));
                location.Create();
                string logFile = Path.Combine(location.FullName, $"{Path.GetFileName(currentAssembly.Location)}_{DateTime.UtcNow.Ticks}_{Process.GetCurrentProcess().Id}.txt");
                using (var fs = new FileStream(HitsFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                using (var log = new FileStream(logFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var logWriter = new StreamWriter(log))
                using (var br = new BinaryReader(fs))
                {
                    int hitsLength = br.ReadInt32();
                    for (int i = 0; i < hitsLength; ++i)
                    {
                        logWriter.WriteLine($"{i},{br.ReadInt32()}");
                    }
                }

                File.AppendAllText(logFile, "Hits flushed");
            }
        }

        private static void WriteLog(string logText)
        {
            if (_enableLog)
            {
                // We don't set path as global var to keep benign possible errors inside try/catch
                // I'm not sure that location will be ok in every scenario
                string location = Assembly.GetExecutingAssembly().Location;
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(location), Path.GetFileName(location) + "_tracker.txt"), $"[{DateTime.UtcNow} P:{Process.GetCurrentProcess().Id} T:{Thread.CurrentThread.ManagedThreadId}]{logText}{Environment.NewLine}");
            }
        }
    }
}
