    public static class BLEConstants
    {
        public class EEGSignal
        {
            public const int NORMAL_EEG_ACQ = 0;
            public const int TEST_EEG_ACQ = 1;
            public static int EEG_SIGNAL_TYPE = NORMAL_EEG_ACQ;
        }

        public class SignalTest
        {
            public static bool ResultReady = false;
            public static bool SuccessResult = true;
            public const int TotalTestBlockCount = 400;
            public const int InitialTestBlocksSkip = 100;
            public const int FinalTestBlocksSkip = 100;
            public static int TestBlockCount = TotalTestBlockCount;
        }

        public static int biopotBits = 16;
    }

