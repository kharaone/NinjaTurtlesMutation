﻿using System;
using System.IO;
using System.IO.Pipes;
using NinjaTurtlesMutation.AppDomainIsolation;
using NinjaTurtlesMutation.AppDomainIsolation.Adaptor;
using NinjaTurtlesMutation.ServiceTestRunnerLib;
using NinjaTurtlesMutation.ServiceTestRunnerLib.Utilities;

namespace NinjaTurtlesMutation.Runner
{
    class Program
    {
        private static float _killTimeFactor;

        static void Main(string[] args)
        {
            if (args.Length != 4)
                return;
            bool oneRunOnly = args[2] == true.ToString();
            _killTimeFactor = float.Parse(args[3]);
            AppDomain.CurrentDomain.UnhandledException += UnexpectedExceptionHandler;
            try
            {
                using (PipeStream receivePipe = new AnonymousPipeClientStream(PipeDirection.In, args[0]))
                using (PipeStream sendPipe = new AnonymousPipeClientStream(PipeDirection.Out, args[1]))
                using (StreamWriter sendStream = new StreamWriter(sendPipe))
                using (StreamReader receiveStream = new StreamReader(receivePipe))
                using (new ErrorModeContext(ErrorModes.FailCriticalErrors | ErrorModes.NoGpFaultErrorBox))
                {
                    while (true)
                    {
                        var testDescription = TestDescriptionExchanger.ReadATestDescription(receiveStream);
                        RunDescribedTests(testDescription);
                        TestDescriptionExchanger.SendATestDescription(sendStream, testDescription);
                        if (oneRunOnly)
                            break;
                    }
                }
            }
            catch (IOException)
            {
                Environment.ExitCode = 1;
            }
            catch
            {
                Environment.ExitCode = 2;
            }
        }

        private static void UnexpectedExceptionHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Environment.Exit(3);
        }

        private static void RunDescribedTests(TestDescription testDescription)
        {
            bool exitedInTime;
            int exitCode;

            using (Isolated<NunitManagedTestRunnerAdaptor> runner = new Isolated<NunitManagedTestRunnerAdaptor>())
            {
                var mutantPath = testDescription.AssemblyPath;
                runner.Instance.Start(mutantPath, testDescription.TestsToRun);
                exitedInTime = runner.Instance.WaitForExit((int)(_killTimeFactor * testDescription.TotalMsBench));
                exitCode = runner.Instance.ExitCode;
            }
            testDescription.ExitedInTime = exitedInTime;
            testDescription.TestsPass = (exitCode == 0);
        }
    }
}
