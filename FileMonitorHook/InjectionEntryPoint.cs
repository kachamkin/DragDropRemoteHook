// RemoteFileMonitor (File: FileMonitorHook\InjectionEntryPoint.cs)
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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FileMonitorHook
{ 
    /// <summary>
    /// EasyHook will look for a class implementing <see cref="EasyHook.IEntryPoint"/> during injection. This
    /// becomes the entry point within the target process after injection is complete.
    /// </summary>
    public class InjectionEntryPoint: EasyHook.IEntryPoint
    {
        /// <summary>
        /// Reference to the server interface within FileMonitor
        /// </summary>
        ServerInterface _server = null;

        /// <summary>
        /// Message queue of all files accessed
        /// </summary>
        Queue<string> _messageQueue = new Queue<string>();

        /// <summary>
        /// EasyHook requires a constructor that matches <paramref name="context"/> and any additional parameters as provided
        /// in the original call to <see cref="EasyHook.RemoteHooking.Inject(int, EasyHook.InjectionOptions, string, string, object[])"/>.
        /// 
        /// Multiple constructors can exist on the same <see cref="EasyHook.IEntryPoint"/>, providing that each one has a corresponding Run method (e.g. <see cref="Run(EasyHook.RemoteHooking.IContext, string)"/>).
        /// </summary>
        /// <param name="context">The RemoteHooking context</param>
        /// <param name="channelName">The name of the IPC channel</param>
        public InjectionEntryPoint(
            EasyHook.RemoteHooking.IContext context,
            string channelName)
        {
            // Connect to server object using provided channel name
            _server = EasyHook.RemoteHooking.IpcConnectClient<ServerInterface>(channelName);

            // If Ping fails then the Run method will be not be called
            _server.Ping();
        }

        /// <summary>
        /// The main entry point for our logic once injected within the target process. 
        /// This is where the hooks will be created, and a loop will be entered until host process exits.
        /// EasyHook requires a matching Run method for the constructor
        /// </summary>
        /// <param name="context">The RemoteHooking context</param>
        /// <param name="channelName">The name of the IPC channel</param>
        public void Run(
            EasyHook.RemoteHooking.IContext context,
            string channelName)
        {
            // Injection is now complete and the server interface is connected
            _server.IsInstalled(EasyHook.RemoteHooking.GetCurrentProcessId());

            // Install hooks

            var hook = EasyHook.LocalHook.Create(EasyHook.LocalHook.GetProcAddress("ole32.dll", "DoDragDrop"),
                new NativeMethods.DragDropDelegate(DoDragDropHook), null);

            // Activate hooks on all threads except the current thread
            hook.ThreadACL.SetExclusiveACL(new Int32[] { 0 });

            _server.ReportMessage("Drag&drop hook installed");

            // Wake up the process (required if using RemoteHooking.CreateAndInject)
            EasyHook.RemoteHooking.WakeUpProcess();

            try
            {
                // Loop until FileMonitor closes (i.e. IPC fails)
                while (true)
                {
                    System.Threading.Thread.Sleep(500);

                    string[] queued = null;

                    lock (_messageQueue)
                    {
                        queued = _messageQueue.ToArray();
                        _messageQueue.Clear();
                    }

                    // Send newly monitored file accesses to FileMonitor
                    if (queued != null && queued.Length > 0)
                    {
                        _server.ReportMessages(queued);
                    }
                    else
                    {
                        _server.Ping();
                    }
                }
            }
            catch
            {
                // Ping() or ReportMessages() will raise an exception if host is unreachable
            }

            // Remove hooks
            hook.Dispose();

            // Finalise cleanup of hooks
            EasyHook.LocalHook.Release();
        }

        #region DragDropHook
        int DoDragDropHook(NativeMethods.IDataObject pDataObj, IntPtr pDropSource, uint dwOKEffects, out uint pdwEffect)
        {
            try
            {
                _messageQueue.Enqueue("Drag started");
                if (!DataObjectHelper.GetDataPresent(pDataObj, "FileGroupDescriptorW") && !DataObjectHelper.GetDataPresent(pDataObj, "FileGroupDescriptor"))
                {
                    _messageQueue.Enqueue("No virtual files found -- continuing original drag");
                    return NativeMethods.DoDragDrop(pDataObj, pDropSource, dwOKEffects, out pdwEffect);
                }

                //Start new drag
                _messageQueue.Enqueue("Virtual files found -- starting new drag adding CF_HDROP format");
                _messageQueue.Enqueue("Files: " + string.Join(",", DataObjectHelper.GetFilenames(pDataObj)));

                OutlookDataObject newDataObj = new OutlookDataObject(pDataObj);
                int result = NativeMethods.DoDragDrop(newDataObj, pDropSource, dwOKEffects, out pdwEffect);

                //If files were dropped and drop effect was "move", then override to "copy" so original item is not deleted
                if (newDataObj.FilesDropped && pdwEffect == NativeMethods.DROPEFFECT_MOVE)
                    pdwEffect = NativeMethods.DROPEFFECT_COPY;

                //Get result
                _messageQueue.Enqueue("DoDragDrop effect: " + pdwEffect + "; result: " + result);
                return result;
            }
            catch (Exception ex)
            {
                _messageQueue.Enqueue("Dragging error" + ex);
                pdwEffect = NativeMethods.DROPEFFECT_NONE;
                return NativeMethods.DRAGDROP_S_CANCEL;
            }
        }

        #endregion
    }
}
