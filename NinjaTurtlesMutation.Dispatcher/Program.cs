﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using NinjaTurtlesMutation.ServiceTestRunnerLib;
using NinjaTurtlesMutation.ServiceTestRunnerLib.Utilities;

namespace NinjaTurtlesMutation.Dispatcher
{
    class Program
    {
        private const int DISPATCH_JOBCHECK_COOLDOWN_MS = 100;
        private const int DISPATCH_RUNNER_ACQUISITION_COOLDOWN_MS = 100;
        private const int SENDING_JOBCHECK_COOLDOWN_MS = 100;

        private static readonly List<TestRunnerHandler> _testRunners  = new List<TestRunnerHandler>();
        private static Task _senderTask;
        private static Task _receiverTask;
        private static Task _cmdTask;

        private static string _pipeInStringHandler;
        private static string _pipeOutStringHandler;
        private static string _pipeCmdStringHandler;

        private static readonly ConcurrentQueue<TestDescription> _unassignedJobs = new ConcurrentQueue<TestDescription>();
        private static readonly ConcurrentDictionary<int, TestDescription> _dispatchedJobs = new ConcurrentDictionary<int, TestDescription>();
        private static readonly ConcurrentQueue<TestDescription> _completedJobs = new ConcurrentQueue<TestDescription>();

        private static bool _shouldStop = false;

        static void Main(string[] args)
        {
            if (args.Length != 4)
                return;
            _pipeInStringHandler = args[0];
            _pipeOutStringHandler = args[1];
            _pipeCmdStringHandler = args[2];
            var numRunners = int.Parse(args[3]);
            InstantiateTestRunners(numRunners);
            InitSender();
            InitReceiver();
            InitCmdReceiver();
            Dispatch();
        }

        private static void Dispatch()
        {
            while (true)
            {
                while (_unassignedJobs.IsEmpty && !_shouldStop)
                    Thread.Sleep(DISPATCH_JOBCHECK_COOLDOWN_MS);
                if (_shouldStop && _unassignedJobs.IsEmpty)
                    break;
                TestDescription testToDispatch = null;
                if (!_unassignedJobs.TryDequeue(out testToDispatch))
                    continue;
                while (testToDispatch != null)
                {
                    var assignedRunnerIndex = -1;
                    while ((assignedRunnerIndex = _testRunners.FindIndex(r => !r.isBusy)) == -1)
                        Thread.Sleep(DISPATCH_RUNNER_ACQUISITION_COOLDOWN_MS);
                    TestRunnerHandler assignedRunner = _testRunners[assignedRunnerIndex];
                    try
                    {
                        assignedRunner.SendJob(testToDispatch);
                    }
                    catch (IOException)
                    {
                        RunnerRestart(assignedRunner, assignedRunnerIndex);
                        continue;
                    }
                    _dispatchedJobs.TryAdd(assignedRunnerIndex, testToDispatch);
                    assignedRunner.isBusy = true;
                    Task.Factory.StartNew(() => BusyRunnerHandler(assignedRunner, assignedRunnerIndex));
                    testToDispatch = null;
                }
            }
        }

        private static void InitReceiver()
        {
            _receiverTask = new Task(ReceivingLoop);
            _receiverTask.Start();
        }

        private static void ReceivingLoop()
        {
            using (PipeStream receivePipe = new AnonymousPipeClientStream(PipeDirection.In, _pipeInStringHandler))
            using (StreamReader receiveStream = new StreamReader(receivePipe))
            {
                while (!_shouldStop)
                {
                    var testDescription = TestDescriptionExchanger.ReadATestDescription(receiveStream);
                    _unassignedJobs.Enqueue(testDescription);
                }
            }
        }

        private static void InitSender()
        {
            _senderTask = new Task(SendingLoop);
            _senderTask.Start();
        }

        private static void InitCmdReceiver()
        {
            _cmdTask = new Task(CmdLoop);
            _cmdTask.Start();
        }

        private static void CmdLoop()
        {
            Dictionary<string, Func<bool>> cmdActions = new Dictionary<string, Func<bool>>();

            cmdActions[CommandExchanger.Commands.STOP] = Stop;
            using (PipeStream receivePipe = new AnonymousPipeClientStream(PipeDirection.In, _pipeCmdStringHandler))
            using (StreamReader receiveStream = new StreamReader(receivePipe))
            {
                while (!_shouldStop)
                {
                    var cmd = CommandExchanger.ReadACommand(receiveStream);
                    cmdActions[cmd]();
                }
            }
        }

        private static bool Stop()
        {
            _shouldStop = true;
            return true;
        }

        private static void InstantiateTestRunners(int numRunners)
        {
            for (int i = 0; i < numRunners; i++)
                _testRunners.Add(new TestRunnerHandler());
        }

        private static void SendingLoop()
        {
            using (PipeStream sendPipe = new AnonymousPipeClientStream(PipeDirection.Out, _pipeOutStringHandler))
            using (StreamWriter sendStream = new StreamWriter(sendPipe))
            {
                while (true)
                {
                    while (_completedJobs.IsEmpty && !_shouldStop)
                        Thread.Sleep(SENDING_JOBCHECK_COOLDOWN_MS);
                    if (_shouldStop && _completedJobs.IsEmpty)
                        break;
                    TestDescription testDescriptionToSend;
                    if (!_completedJobs.TryDequeue(out testDescriptionToSend))
                        continue;
                    TestDescriptionExchanger.SendATestDescription(sendStream, testDescriptionToSend);
                }
            }
        }

        private static void BusyRunnerHandler(TestRunnerHandler busyRunner, int busyRunnerIndex)
        {
            TestDescription sink;
            var testResult = RetrieveTestResult(busyRunner, busyRunnerIndex);
            _completedJobs.Enqueue(testResult);
            _dispatchedJobs.TryRemove(busyRunnerIndex, out sink);
            busyRunner.isBusy = false;
        }

        private static TestDescription RetrieveTestResult(TestRunnerHandler busyRunner, int busyRunnerIndex)
        {
            try
            {
                return (busyRunner.GetTestResult());
            }
            catch (IOException)
            {
                var testResult = _dispatchedJobs[busyRunnerIndex];
                testResult.TestsPass = false;
                return (testResult);
            }
        }

        private static void RunnerHealthCheckup(TestRunnerHandler busyRunner, int busyRunnerIndex)
        {
            if (busyRunner.IsAlive())
            {
                busyRunner.isBusy = false;
                return ;
            }
            busyRunner.KillTestRunner();
            _testRunners[busyRunnerIndex] = new TestRunnerHandler();
        }

        private static void RunnerRestart(TestRunnerHandler busyRunner, int busyRunnerIndex)
        {
            busyRunner.KillTestRunner();
            _testRunners[busyRunnerIndex] = new TestRunnerHandler();
        }
    }
}