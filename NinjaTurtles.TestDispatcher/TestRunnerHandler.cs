﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinjaTurtles.ServiceTestRunnerLib;
using NinjaTurtles.ServiceTestRunnerLib.Utilities;

namespace NinjaTurtles.TestDispatcher
{
    internal class TestRunnerHandler
    {
        private readonly Process _runnerProcess;
        private readonly AnonymousPipeServerStream _runnerPipeIn;
        private readonly AnonymousPipeServerStream _runnerPipeOut;

        private readonly object _lockObject = new object();

        public StreamReader runnerStreamIn;
        public StreamWriter runnerStreamOut;

        public bool isBusy;

        public TestRunnerHandler()
        {
            _runnerPipeIn = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            _runnerPipeOut = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            runnerStreamIn = new StreamReader(_runnerPipeIn);
            runnerStreamOut = new StreamWriter(_runnerPipeOut);
            _runnerProcess = new Process();
            _runnerProcess.StartInfo.FileName = "testrunner.exe";
            _runnerProcess.StartInfo.UseShellExecute = false;
            _runnerProcess.StartInfo.Arguments = _runnerPipeOut.GetClientHandleAsString() + " " +
                                                _runnerPipeIn.GetClientHandleAsString();
            _runnerProcess.Start();
            _runnerPipeOut.DisposeLocalCopyOfClientHandle();
            _runnerPipeIn.DisposeLocalCopyOfClientHandle();
            isBusy = false;
        }

        public void KillTestRunner()
        {
            try
            {
                _runnerProcess.Kill();
                runnerStreamIn.Dispose();
                runnerStreamOut.Dispose();
                _runnerPipeIn.Dispose();
                _runnerPipeOut.Dispose();
            }
            catch { }
        }

        public void SendJob(TestDescription job)
        {
            TestDescriptionExchanger.SendATestDescription(runnerStreamOut, job);
        }

        public bool IsAlive()
        {
            try
            {
                runnerStreamIn.Peek();
                runnerStreamOut.Flush();
                return (true);
            }
            catch (IOException)
            {
                return (false);
            }
        }

        public TestDescription GetTestResult()
        {
            return (TestDescriptionExchanger.ReadATestDescription(runnerStreamIn));
        }
    }
}