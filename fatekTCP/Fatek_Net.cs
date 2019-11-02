using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using System.Net.Sockets;
using System.Net;
using System.Linq;
using System.Diagnostics;

namespace fatekTCP
{
    class Fatek_Net
    {
        public Fatek_Net(int nStationID, string strIP, int nPort)
        {
            this._PLCInfo = new PLCInfo();
            this._PLCInfo.setStationID(nStationID);

            this._PLCIP = IPAddress.Parse(strIP);
            this._PORT = nPort;
            this._ipe = new IPEndPoint(_PLCIP, nPort);

            _SocketPLC = new Socket(_ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            // _SocketPLC = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        }
        ~Fatek_Net()
        {
            if (_SocketPLC != null)
            {
                _SocketPLC.Shutdown(SocketShutdown.Both);
                _SocketPLC.Close();
            }
        }

        public bool isConnected { get { return _SocketPLC.Connected; } }

        private Socket _SocketPLC;
        private PLCInfo _PLCInfo;
        // private IPHostEntry hostEntry;
        private IPAddress _PLCIP;                       // = IPAddress.Parse("192.168.2.3");
        private int _PORT;
        private IPEndPoint _ipe;                        // = new IPEndPoint(_PLCIP, 500);

        private static readonly object _connectLock = new object();        // to prevent multiple threads from executing the same function at the same time.
        private static readonly object _sendLock = new object();

        public void ConnectPLCAsync(Action<bool> callbackFunc, int nTimeout = 5000)
        {

            var t1 = Task.Factory.StartNew(() =>
            {

                var sw = Stopwatch.StartNew();

                while (!_SocketPLC.Connected)
                {
                    if (sw.ElapsedMilliseconds >= nTimeout)
                        break;

                    ConnectPLC();
                    Thread.Sleep(1000);
                }

                // sw.Stop();
                callbackFunc(_SocketPLC.Connected);
            });

        }

        public bool ConnectPLC()
        {
            lock (_connectLock)
            {
                try
                {
                    //_SocketPLC.Connect(IPAddress.Loopback, _PORT);
                    _SocketPLC.Connect(_ipe);
                    
                }
                catch (SocketException)
                {

                }
                catch (Exception err)
                {

                }

                return _SocketPLC.Connected;
            }
        }

        private bool _reConnectPLC()
        {
            while (!_SocketPLC.Connected)
            {
                Console.WriteLine("Reconnect to PLC...");
                ConnectPLC();
                Thread.Sleep(1000);
            }   

            return _SocketPLC.Connected;
        }

        private byte[] SendPLC(PLCInfo.FunctionID cmd, string strMsg)
        {
            lock (_sendLock)
            {
                byte[] baRcvBuffer = new byte[512];
                byte[] baUsefulRes = new byte[0];

                _PLCInfo.strCommand = ((int)cmd).ToString();

                string strExpectedRes = _PLCInfo.strStationID + _PLCInfo.strCommand;
                string strControlCmd = strExpectedRes + strMsg;
                var finalCmd = PLCInfo.combineCommand(strControlCmd, strExpectedRes);           // baExpectedRes: 0230313430
                byte[] baSendCom = finalCmd.Item1;
                byte[] baExpectedRes = finalCmd.Item2;

                try
                {
                    _SocketPLC.Send(baSendCom, baSendCom.Length, 0);                            // command sent: 0230313430433703

                    int nRcvBytesLength = _SocketPLC.Receive(baRcvBuffer, SocketFlags.None);
                    byte[] baPLCRes = new byte[nRcvBytesLength];                                // baPLCRes: 023031343030303134303030314303
                    Array.Copy(baRcvBuffer, baPLCRes, nRcvBytesLength);

                    // Remove STX, Original cmmand, Checksum and ETX
                    baUsefulRes = RemoveControlCharChksum(baPLCRes, baExpectedRes);             // baUsefulRes: 30303134303030

                }
                catch (SocketException)
                {
                    _reConnectPLC();
                }
                catch (Exception err)
                {
                    //MessageBox.Show(err.Message);
                    Console.WriteLine(err.Message);

                }

                return baUsefulRes;
            }
        }

        public Tuple<ErrorCode, byte[]> getPLCInfo()
        {
            byte[] baPLCAsciiRes = SendPLC(PLCInfo.FunctionID.ReadSystem, String.Empty);

            if (baPLCAsciiRes.Length > 0)
            {
                // baPLCAsciiRes: 30303134303030
                // strUsefulRes: 0014000
                string strUsefulRes = Encoding.ASCII.GetString(baPLCAsciiRes);
                byte[] baUsefulRes = decStringToDecByteArray(strUsefulRes);
                byte[] baPLCState = (baUsefulRes[0] == 0) ? baUsefulRes.Skip(1).Take(baUsefulRes.GetLength(0) - 1).ToArray<byte>() : new byte[0];

                return Tuple.Create(myErrorCode(baUsefulRes[0]), baPLCState);
            }
            else
            {
                return Tuple.Create(ErrorCode.PLCSocketErr, new byte[0]);
            }
        }

        public void getPLCInfoAsync(Action<ErrorCode, byte[]> callbackFunc)
        {
            var t1 = Task.Factory.StartNew(() =>{

                var result = getPLCInfo();
                ErrorCode errorCode = result.Item1;
                byte[] baPLCState = result.Item2;
                callbackFunc(errorCode, baPLCState);
                
            });
                
        }

        public Tuple<ErrorCode, byte> getCoilState(PLCInfo.CoilType coilType, int nCoilNumber)
        {
            
            var result = getCoilsState(coilType, nCoilNumber, 1);
            ErrorCode errorCode = result.Item1;
            byte nCoilState;
            if ((int)errorCode == 0)
                nCoilState = result.Item2[0];
            else
                nCoilState = 0;

            return Tuple.Create(errorCode, nCoilState);        // first one is error code, the second rest are useful info.
        }

        public void getCoilStateAsync(Action<ErrorCode, byte> callbackFunc, PLCInfo.CoilType coilType, int nCoilNumber)
        {
            var t1 = Task.Factory.StartNew(() =>
            {
                var result = getCoilState(coilType, nCoilNumber);
                ErrorCode errorCode = result.Item1;
                byte nCoilState = result.Item2;
                callbackFunc(errorCode, nCoilState);
            });
        }

        public Tuple<ErrorCode, byte[]> getCoilsState(PLCInfo.CoilType coilType, int nCoilNumber, int nReadNumber)
        {
            string strType = coilType.ToString();
            string strReadNumber = nReadNumber.ToString("D2");
            string strCoilNumber = nCoilNumber.ToString("D4");
            string strCmd = strReadNumber + strType + strCoilNumber;
            byte[] baPLCAsciiRes = SendPLC(PLCInfo.FunctionID.ReadStatePoint, strCmd);

            if (baPLCAsciiRes.Length > 0)
            {
                string strUsefulRes = Encoding.ASCII.GetString(baPLCAsciiRes);
                // Suppose M0=1, M1=0, M2=1, M3=1, strUsefulRes= 01011
                // Note: The first 0 is error code. and the rest bits are for M0~M3

                byte[] baUsefulRes = decStringToDecByteArray(strUsefulRes);
                byte[] baResult = (baUsefulRes[0] == 0) ? baUsefulRes.Skip(1).Take(baUsefulRes.GetLength(0) - 1).ToArray<byte>() : new byte[0];

                return Tuple.Create(myErrorCode(baUsefulRes[0]), baResult);        // first one is error code, the second rest are useful info.
            }
            else
            {
                return Tuple.Create(ErrorCode.PLCSocketErr, new byte[0]);
            }
        }

        public void getCoilsStateAsync(Action<ErrorCode, byte[]> callbackFunc, PLCInfo.CoilType coilType, int nCoilNumber, int nReadNumber)
        {
            var t1 = Task.Factory.StartNew(() =>
            {
                var result = getCoilsState(coilType, nCoilNumber, nReadNumber);
                ErrorCode errorCode = result.Item1;
                byte[] baCoilState = result.Item2;
                callbackFunc(errorCode, baCoilState);
            });
        }

        public Tuple<ErrorCode, long> getRegValue(PLCInfo.RegType regType, int nRegNumber)
        {
            var result = getRegsValue(regType, nRegNumber, 1);
            ErrorCode errorCode = result.Item1;
            long nRegValue;
            if ((int)errorCode == 0)
                nRegValue = result.Item2[0];
            else
                nRegValue = 0;
             

            return Tuple.Create(errorCode, nRegValue);        // first one is error code, the second rest are useful info.
        }

        public void getRegValueAsync(Action<ErrorCode, long> callbackFunc, PLCInfo.RegType regType, int nRegNumber)
        {
            var t1 = Task.Factory.StartNew(() =>
            {
                var result = getRegValue(regType, nRegNumber);
                ErrorCode errorCode = result.Item1;
                long nRegValue = result.Item2;
                callbackFunc(errorCode, nRegValue);
            });
        }

        public Tuple<ErrorCode, long[]> getRegsValue(PLCInfo.RegType regType, int nRegNumber, int nReadNumber)
        {
            string strType = regType.ToString();
            string strReadNumber = nReadNumber.ToString("D2");
            string strRegNumber = nRegNumber.ToString("D5");
            
            string strCmd = strReadNumber + strType + strRegNumber;
            byte[] baPLCAsciiRes = SendPLC(PLCInfo.FunctionID.ReadReg, strCmd);

            if (baPLCAsciiRes.Length > 0)
            {
                string strUsefulRes = Encoding.ASCII.GetString(baPLCAsciiRes);

                // Example: R0 = 1000, strUsefulRes= 003E8  DR2 = 10,000,000, strUsefulRes = 000989680
                // Note: The first 0 is error code. so it actually is "03E8" for R0 = 1000.
                byte nErrorCode = byte.Parse(strUsefulRes[0].ToString());

                byte[] baUsefulRes = new byte[0];
                long[] naResult = new long[0];
                if (nErrorCode == 0)
                {
                    baUsefulRes = hexStringToHexByteArray(strUsefulRes.Substring(1));
                    int nDataLength = 0;

                    switch (regType)
                    {
                        case PLCInfo.RegType.R:
                        case PLCInfo.RegType.D:
                            nDataLength = 2;
                            break;
                        case PLCInfo.RegType.DR:
                        case PLCInfo.RegType.DD:
                            nDataLength = 4;
                            break;
                        default:
                            return Tuple.Create(ErrorCode.PLCInfoTypeErr, naResult);
                    }

                    int nValueLength = baUsefulRes.Length / nDataLength;
                    long[] naTemp = new long[nValueLength];
                    for (int i = 0; i < nValueLength; i += nDataLength)
                    {
                        byte[] baTemp = baUsefulRes.Skip(i * nDataLength).Take(nDataLength).ToArray<byte>();

                        // Since Fatek PLC is Little-endian-based, must reverse it first
                        Array.Reverse(baTemp);

                        if (nDataLength == 2)
                        {
                            Int16 nRegValue = BitConverter.ToInt16(baTemp, 0);
                            naTemp[i] = nRegValue;
                        }
                        else
                        {
                            Int32 nRegValue = BitConverter.ToInt32(baTemp, 0);
                            naTemp[i] = nRegValue;
                        }

                    }
                    naResult = naTemp;

                }

                return Tuple.Create(myErrorCode(nErrorCode), naResult);        // first one is error code, the second rest are useful info.
            }
            else
            {
                return Tuple.Create(ErrorCode.PLCSocketErr, new long[0]);
            }
        }

        public void getRegsValueAsync(Action<ErrorCode, long[]> callbackFunc, PLCInfo.RegType regType, int nRegNumber, int nReadNumber)
        {
            var t1 = Task.Factory.StartNew(() =>
            {
                var result = getRegsValue(regType, nRegNumber, nReadNumber);
                ErrorCode errorCode = result.Item1;
                long[] naRegValue = result.Item2;
                callbackFunc(errorCode, naRegValue);
            });
        }

        public ErrorCode setRegValue(PLCInfo.RegType regType, int nRegNumber, long nWriteValue)
        {
            ErrorCode nErrorCode = setRegsValue(regType, nRegNumber, 1, new long[]{nWriteValue});
            return nErrorCode;
        }

        public void setRegValueAsync(Action<ErrorCode> callbackFunc, PLCInfo.RegType regType, int nRegNumber, long nWriteValue)
        {
            var t1 = Task.Factory.StartNew(() =>
            {
                var errorCode = setRegValue(regType, nRegNumber, nWriteValue);
                callbackFunc(errorCode);
            });
        }

        public ErrorCode setRegsValue(PLCInfo.RegType regType, int nRegNumber, int nWriteNumber, long[] naWriteValues)
        {
            // Example: naWriteValues = [1000, 12000] ==>  "03E8" + "2EE0" = "03E82EE0"
            string strType = regType.ToString();
            string strWriteNumber = nWriteNumber.ToString("D2");
            string strRegNumber = nRegNumber.ToString("D5");
            
            ErrorCode errorCode = ErrorCode.None;
            // long[] naResult = new long[0];
            
            int nDataLength = 0;
            switch(regType)
            {
                case PLCInfo.RegType.R:
                case PLCInfo.RegType.D:
                    nDataLength = 4;
                    break;
                case PLCInfo.RegType.DR:
                case PLCInfo.RegType.DD:
                    nDataLength = 8;
                    break;
                default:
                    return ErrorCode.PLCInfoTypeErr;
            }

            string strWriteValue = ByteArrayToHexString(naWriteValues, nDataLength);

            string strCmd = strWriteNumber + strType + strRegNumber + strWriteValue;
            byte[] baPLCAsciiRes = SendPLC(PLCInfo.FunctionID.WriteReg, strCmd);
            if (baPLCAsciiRes.Length > 0)
            {
                string strUsefulRes = Encoding.ASCII.GetString(baPLCAsciiRes);
                byte nErrorCode = byte.Parse(strUsefulRes[0].ToString());
                errorCode = myErrorCode(nErrorCode);
            }
            else
                errorCode = ErrorCode.PLCSocketErr;

            return errorCode;
        }

        public void setRegsValueAsync(Action<ErrorCode> callbackFunc, PLCInfo.RegType regType, int nRegNumber, int nWriteNumber, long[] naWriteValues)
        {
            var t1 = Task.Factory.StartNew(() =>
            {
                var errorCode = setRegsValue(regType, nRegNumber, nWriteNumber, naWriteValues);
                callbackFunc(errorCode);
            });
        }

        private static byte[] RemoveControlCharChksum(byte[] baSource, byte[] baExpectedRes)
        {
            
            int nSourceLength = baSource.GetLength(0);
            int nExpectedLength = baExpectedRes.GetLength(0);
            int nNewLength = nSourceLength - nExpectedLength - 3;       // extra length 3 is for checkSum (takes up 2 bytes) and ETX(1 byte)

            byte[] baUsefulRes = new byte[nNewLength];   

            for (int i = 0; i < nNewLength; i++)
            {
                baUsefulRes[i] = baSource[nExpectedLength + i];
            }

            return baUsefulRes;
        }

        private static string RemoveControlCharAndLetter(string strSource, int nOption)
        {
            // Method 1
            //string output = new string(strSource.Where(c => char.IsLetter(c) || char.IsDigit(c)).ToArray());

            // Method 2
            if (strSource == null) return null;
            StringBuilder newString = new StringBuilder();
            char ch;
            for (int i = 0; i < strSource.Length; i++)
            {
                ch = strSource[i];
                switch (nOption)
                {
                    case 0:
                        if (!char.IsControl(ch))
                        {
                            newString.Append(ch);
                        }
                        break;
                    case 1:
                    default:
                        if (!char.IsControl(ch) && !char.IsLetter(ch))
                        {
                            newString.Append(ch);
                        }
                        break;
                    
                }
                
            }
            return newString.ToString();
        }

        private static byte[] hexStringToHexByteArray(string strSource)
        {
            // Example: "03E8" ==> byte[0x03, 0xE8], "00989680" ==> byte[0x00, 0x98, 0x96, 0x80]

            var baResult = Enumerable.Range(0, strSource.Length / 2)
                             .Select(x => Convert.ToByte(strSource.Substring(x * 2, 2), 16))
                             .ToArray();

            return baResult;

        }

        private static byte[] decStringToDecByteArray(string strSource)
        {
            // Example: "012345"  ==> [0, 1, 2, 3, 4, 5]
            int nLength = strSource.Length;
            byte[] baUsefulRes = new byte[nLength];
            for (int i = 0; i < nLength; i++)
            {
                baUsefulRes[i] = byte.Parse(strSource[i].ToString());
            }

            return baUsefulRes;
        }

        private static byte[] ASCIIArrayToIntArray(byte[] ba)
        {
            // ba: 023031343030303134303030314303
            // strDecValue: 014000140001C
            string strDecValue = Encoding.ASCII.GetString(ba);
            string strUsefulInfo =  RemoveControlCharAndLetter(strDecValue, 1);

            //int nDevValue = 0;
            //Int32.TryParse(strUsefulInfo, out nDevValue);


            byte[] baDevValue = new byte[strUsefulInfo.Length];

            for (int i = 0; i < strUsefulInfo.Length; i++)
            {
                baDevValue[i] = byte.Parse(strUsefulInfo[i].ToString());
            }

            return baDevValue;
        }

        private static string DecIntToHexString(int nDecValue)
        {
            //Example1: nDecValue = 1000 ==> hexString = "3E8", nDecValue = 10,000,000 ==> hexString = "989680"
            
            // Method1
            //string hexString = nDecValue.ToString("X");      // X or X2 both work

            //Example1: nDecValue = 1000 ==> hexString = "0x3E8", nDecValue = 10,000,000 ==> hexString = "0x989680"
            // Method2
            string hexString = String.Format("0x{0:X}", nDecValue); 
            return hexString;
        }

        private static int HexStringToDecInt(string strHexValue)
        {
            // Example: strHexValue = "0x3E8" or "3E8" ==> nDecValue = 1000

            // strip the leading 0x
            if (strHexValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                strHexValue = strHexValue.Substring(2);
            }

            // Method1
            int nDecValue = Convert.ToInt32(strHexValue, 16);  // Back to int again.

            // Method2
            //int nDecValue = Int32.Parse(strValue, System.Globalization.NumberStyles.HexNumber);

            return nDecValue;
        }

        private static string ByteArrayToHexString<T>(T[] ba, int nFmtLength = 2)
        {
            // Both Decimal format or Hexadecimal one work.
            // Example: ba = [0x02, 0x30, 0x31, 0x34, 0x30] ==> hexString = "0230313430"
            // Example: ba = [2, 48, 49, 52, 48] ==> hexString = "0230313430"

            // Method1 BitConverter.ToString(ba) ==> "02-30-31-34-30"
            //string hexString = BitConverter.ToString(ba).Replace("-", string.Empty);

            string fmt = String.Format("X{0}", nFmtLength);     // Could be "X2", "X4" or "X8"
            fmt = "{0:" + fmt + "}";

            // Method2
            StringBuilder hexString = new StringBuilder();
            foreach (T b in ba)
            {
                hexString.AppendFormat(fmt, b);
                // hexString.AppendFormat("{0:D}", b);         // ba = [0x02, 0x30, 0x31, 0x34, 0x30] ==> hexString = "248495248"
            }

            return hexString.ToString();
        }

        private static byte[] HexStringToByteArray(String hexString)
        {
            // Example: hexString = "0230313430" ==> hexBytes = [0x02, 0x30, 0x31, 0x34, 0x30]
            int NumberChars = hexString.Length;
            byte[] hexBytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                hexBytes[i / 2] = Convert.ToByte(hexString.Substring(i, 2), 16);
            return hexBytes;
        }

        private ErrorCode myErrorCode(byte bErrorCode)
        {
            ErrorCode errorCode;
            int nErrorCode = Convert.ToInt32(bErrorCode);
            if (Enum.IsDefined(typeof(ErrorCode), nErrorCode))
                errorCode = (ErrorCode)nErrorCode;
            else
                errorCode = ErrorCode.Unknown;
            return errorCode;
        }
    }


    class PLCInfo
    {
        public enum FunctionID
        {
            ReadSystem = 40,
            PLC_StartUp = 41,
            SinglePointCtrl = 42,
            ReadEnablePoint = 43,
            ReadStatePoint = 44,
            WriteStatePoint = 45,
            ReadReg = 46,
            WriteReg = 47,
            ReadRegPoint = 48,
            WriteRegPoint = 49,
            //TestCom = 4E,
            LoadProgram = 50,
            ReadSystemState = 53
        }

        public enum StartUp
        {
            STOP = 0,
            RUN = 1
        }

        public enum Control
        {
            Disable = 1,
            Enable = 2,
            Set = 3,
            Reset = 4
        }

        public enum CoilType
        {
            X = 0,
            Y,
            M
        }

        public enum RegType
        {
            R = 0,
            D,
            DR,
            DD
        }

        public string strStationID;
        public string strCommand;
        

        public void setStationID(int nId)
        {
            strStationID = String.Format("{0:X2}", nId);
        }

        //// This is check sum (Longitudinal Redundancy Check)
        private static byte[] calculateLRC(byte[] bytes)
        {
            byte LRC = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                LRC += bytes[i];
            }
            byte[] byteConverterUse = new byte[1];
            byteConverterUse[0] = LRC;
            return Encoding.ASCII.GetBytes(BitConverter.ToString(byteConverterUse));
        }
        public static Tuple<byte[], byte[]> combineCommand(string strControlCmd, string strExpectedRes)
        {
            byte[] baControlCmd = Encoding.ASCII.GetBytes(strControlCmd);
            int nCmdLength = baControlCmd.GetLength(0);

            byte[] baTemp = Encoding.ASCII.GetBytes(strExpectedRes);
            byte[] baExpectedRes = new byte[baTemp.GetLength(0) + 1];
            baExpectedRes[0] = (byte)0x02;
            baTemp.CopyTo(baExpectedRes, 1);

            byte[] baCombineCommand = new byte[nCmdLength + 4];        // extra length 4 is for STX(1 byte), checkSum (takes up 2 bytes), ETX(1 byte)
            baCombineCommand[0] = (byte)0x02;
            baControlCmd.CopyTo(baCombineCommand, 1);

            byte[] ChkSUM = calculateLRC(baCombineCommand);
            baCombineCommand[nCmdLength + 1] = ChkSUM[0];
            baCombineCommand[nCmdLength + 2] = ChkSUM[1];
            baCombineCommand[nCmdLength + 3] = (byte)0x03;

            return Tuple.Create(baCombineCommand, baExpectedRes);
        }
    }

    public enum ErrorCode
    {
        None = 0,
        
        // PLC internal Error
        PLCInternalErr_Spare1,
        PLCInternalErr_InvalidValue,
        PLCInternalErr_Spare3,
        PLCInternalErr_InvalidFormat,
        PLCInternalErr_InvalidChecksum,
        PLCInternalErr_InvalidPLCID,
        PLCInternalErr_SyntaxErr,
        PLCInternalErr_Spare8,
        PLCInternalErr_CannotExecute,
        PLCInternalErr_InvalidAddress,

        // User-defined Error
        Unknown = -9000,
        PLCSocketErr,
        PLCInfoTypeErr
    }

    public static class ErrorMessage
    {
        public static string ToString(this ErrorCode err)
        {
            switch (err)
            {
                case ErrorCode.None:
                    return "None";

                case ErrorCode.PLCSocketErr:
                    return "PLC Socket error";

                case ErrorCode.PLCInternalErr_InvalidValue:
                    return "The value you specified is Invalid";

                case ErrorCode.PLCInternalErr_InvalidFormat:
                    return "The communication format is Invalid";

                case ErrorCode.PLCInternalErr_InvalidChecksum:
                    return "The checksum in PLC Ladder program in not matched while sending out 'Run' command";

                case ErrorCode.PLCInternalErr_InvalidPLCID:
                    return "PLC ID in not matched with Ladder ID";

                case ErrorCode.PLCInternalErr_SyntaxErr:
                    return "Syntax error while sending out 'Run' command";

                case ErrorCode.PLCInternalErr_CannotExecute:
                    return "Unknown command in PLC Ladder";

                case ErrorCode.PLCInternalErr_InvalidAddress:
                    return "Invalid memory address";

                case ErrorCode.PLCInfoTypeErr:
                    return "The register or coil type you specified is not existing";

                default:
                    return "This error message has not been defined yet";
            }
        }
    }
}
