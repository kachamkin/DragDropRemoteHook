// RemoteFileMonitor (File: FileMonitor\Program.cs)
//
// Copyright (c) 2017 Justin Stenning
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
// Please visit https://easyhook.github.io for more information
// about the project, latest updates and other tutorials.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using log4net;
using log4net.Config;

namespace FileMonitor
{
    class Program
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static int procID = 0;
        private static string procName = "";

        static void Main(string[] args)
        {
            
            // Process command line arguments or print instructions and retrieve argument value
            ProcessArgs(args, out int targetPID);

            if (targetPID <= 0)
                return;

            SetHook(targetPID);

        }

        static void SetHook(int targetPID)
        {
            // Will contain the name of the IPC server channel
            string channelName = null;

            // Create the IPC server using the FileMonitorIPC.ServiceInterface class as a singleton
            EasyHook.RemoteHooking.IpcCreateServer<FileMonitorHook.ServerInterface>(ref channelName, System.Runtime.Remoting.WellKnownObjectMode.Singleton);

            // Get the full path to the assembly we want to inject into the target process
            string injectionLibrary = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "FileMonitorHook.dll");

            try
            {
                // Injecting into existing process by Id
                if (targetPID > 0)
                {
                    log.Info("Attempting to inject into process " + targetPID);

                    // inject into existing process
                    EasyHook.RemoteHooking.Inject(
                        targetPID,          // ID of process to inject into
                        injectionLibrary,   // 32-bit library to inject (if target is 32-bit)
                        injectionLibrary,   // 64-bit library to inject (if target is 64-bit)
                        channelName         // the parameters to pass into injected library
                                            // ...
                    );
                }
            }
            catch (Exception e)
            {
                log.Error("There was an error while injecting into target:\r\n" + e.ToString());
            }

            procID = targetPID;
            WaitForProc();

        }

        static async void WaitForProc()
        {
            await SetHookAsync();
        }

        static Task SetHookAsync()
        {
            int id = GetProcessId();
            if (id != 0 && id != procID)
                SetHook(id);
            else
            {
                Thread.Sleep(500);
                return SetHookAsync();
            }
            return null;
        }

        static void ProcessArgs(string[] args, out int targetPID)
        {
            targetPID = 0;

            // Load any parameters
            if (args.Length == 0)
            {
                procName = "outlook";
                SetHookAsync();
            }
            else if (args.Length > 1)
            {
                log.Error(@"Usage: FileMonitor ProcessFriendlyName
                e.g. : FileMonitor outlook");
            }
            else
            {
                procName = args[0];
                SetHookAsync();
            }
        }

        static int GetProcessId()
        {
            Process[] processes = Process.GetProcessesByName(procName);
            return processes.Length > 0 ? processes[0].Id : 0;
        }

    }
}
