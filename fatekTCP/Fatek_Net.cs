using System;
using System.Collections.Generic;
using System.Text;


using System.Net.Sockets;
using System.Net;
using System.Linq;

namespace fatekTCP
{
    class Fatek_Net
    {
        public Fatek_Net(int nStationID, string strIP, int nPort)
        {
            this.PLCInfo = new PLCInfo();
            this.PLCInfo.Set_StationID(nStationID);

            this._PLCIP = IPAddress.Parse(strIP);
            this._ipe = new IPEndPoint(_PLCIP, nPort);
        }

         
        public Socket SocketPLC;
        public PLCInfo PLCInfo;
        
        //public IPHostEntry hostEntry;
        private IPAddress _PLCIP;           // = IPAddress.Parse("192.168.2.3");
        private IPEndPoint _ipe;                     // = new IPEndPoint(_PLCIP, 500);
        

        public bool ConnectPLC()
        {
            bool bConnectPLC = false;

            try
            {
                
                Socket tempSocket = new Socket(_ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                tempSocket.Connect(_ipe);
                SocketPLC = tempSocket;
                //SocketPLC.Connect(ipe);
                bConnectPLC = true;
            }
            catch (Exception err)
            {
                bConnectPLC = false;
            }
            return bConnectPLC;
        }

        private byte[] SendPLC(PLCInfo.FunctionID cmd, string strMsg)
        {
            
            byte[] baRcvBuffer = new byte[512];
            byte[] baUsefulRes = new byte[0];


            PLCInfo.strCommand = ((int)cmd).ToString();

            string strExpectedRes = PLCInfo.strStationID + PLCInfo.strCommand;
            string strControlCmd = strExpectedRes + strMsg;
            var finalCmd = PLCInfo.CombineCommand(strControlCmd, strExpectedRes);           // baExpectedRes: 0230313430
            byte[] baSendCom = finalCmd.Item1;
            byte[] baExpectedRes = finalCmd.Item2;

            try
            {
                SocketPLC.Send(baSendCom, baSendCom.Length, 0);
                Console.WriteLine($"command sent: {ByteArrayToHexString(baSendCom)}");         // command sent: 0230313430433703

                int nRcvBytesLength = SocketPLC.Receive(baRcvBuffer, SocketFlags.None);
                Console.WriteLine($"nRcvBytesLength: {nRcvBytesLength}");
                byte[] baPLCRes = new byte[nRcvBytesLength];
                Array.Copy(baRcvBuffer, baPLCRes, nRcvBytesLength);
                Console.WriteLine($"baPLCRes: {ByteArrayToHexString(baPLCRes)}");              // baPLCRes: 023031343030303134303030314303


                // Remove STX, Original cmmand, Checksum and ETX
                baUsefulRes = RemoveControlCharChksum(baPLCRes, baExpectedRes);             // baUsefulRes: 30303134303030


            }
            catch (Exception err)
            {
                //MessageBox.Show(err.Message);
                Console.WriteLine(err.Message);
            }
            
            return baUsefulRes;

        }

        public Tuple<byte, byte[]> getPLCInfo()
        {
            byte[] baPLCAsciiRes = SendPLC(PLCInfo.FunctionID.ReadSystem, String.Empty);

            // baPLCAsciiRes: 30303134303030
            // strUsefulRes: 0014000
            string strUsefulRes = Encoding.ASCII.GetString(baPLCAsciiRes);
            Console.WriteLine($"strUsefulRes: {strUsefulRes}");
            byte[] baUsefulRes = decStringToDecByteArray(strUsefulRes);
            byte[] baPLCState = (baUsefulRes[0] == 0) ? baUsefulRes.Skip(1).Take(baUsefulRes.GetLength(0) - 1).ToArray<byte>() : new byte[0];

            return Tuple.Create(baUsefulRes[0], baPLCState);
        }

        public Tuple<byte, byte> getCoilState(PLCInfo.CoilType coilType, int nCoilNumber)
        {
            
            var result = getCoilsState(coilType, nCoilNumber, 1);
            byte nErrorCode = result.Item1;
            byte nCoilState = result.Item2[0];

            return Tuple.Create(nErrorCode, nCoilState);        // first one is error code, the second rest are useful info.
        }

        public Tuple<byte, byte[]> getCoilsState(PLCInfo.CoilType coilType, int nCoilNumber, int nReadNumber)
        {
            string strType = coilType.ToString();
            string strReadNumber = nReadNumber.ToString("D2");
            string strCoilNumber = nCoilNumber.ToString("D4");
            string strCmd = strReadNumber + strType + strCoilNumber;
            byte[] baPLCAsciiRes = SendPLC(PLCInfo.FunctionID.ReadStatePoint, strCmd);
            string strUsefulRes = Encoding.ASCII.GetString(baPLCAsciiRes);

            Console.WriteLine("strUsefulRes: {0}, {1}", strUsefulRes, strUsefulRes.Length);

            byte[] baUsefulRes = decStringToDecByteArray(strUsefulRes);

            byte[] baResult = (baUsefulRes[0] == 0) ? baUsefulRes.Skip(1).Take(baUsefulRes.GetLength(0) - 1).ToArray<byte>() : new byte[0];

            return Tuple.Create(baUsefulRes[0], baResult);        // first one is error code, the second rest are useful info.
        }

        public Tuple<byte, long> getRegValue(PLCInfo.RegType regType, int nRegNumber)
        {
            var result = getRegsValue(regType, nRegNumber, 1);
            byte nErrorCode = result.Item1;
            long nRegValue = result.Item2[0];

            return Tuple.Create(nErrorCode, nRegValue);        // first one is error code, the second rest are useful info.
        }

        public Tuple<byte, long[]> getRegsValue(PLCInfo.RegType regType, int nRegNumber, int nReadNumber)
        {
            string strType = regType.ToString();
            string strReadNumber = nReadNumber.ToString("D2");
            string strRegNumber = nRegNumber.ToString("D5");
            
            string strCmd = strReadNumber + strType + strRegNumber;
            byte[] baPLCAsciiRes = SendPLC(PLCInfo.FunctionID.ReadReg, strCmd);
            string strUsefulRes = Encoding.ASCII.GetString(baPLCAsciiRes);

            Console.WriteLine("strUsefulRes: {0}, {1}", strUsefulRes, strUsefulRes.Length);

            // Example: R0 = 1000, strUsefulRes= 003E8  DR2 = 10,000,000, strUsefulRes = 000989680
            // Note: The first 0 is error code. so it actually is "03E8" for R0 = 1000.
            byte nErrorCode = byte.Parse(strUsefulRes[0].ToString());
            Console.WriteLine($"nErrorCode: {nErrorCode}");

            byte[] baUsefulRes = new byte[0];
            long[] naResult = new long[0];
            if (nErrorCode == 0)
            {
                baUsefulRes = hexStringToHexByteArray(strUsefulRes.Substring(1));
                int nDataLength = 0;

                switch(regType)
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
                        return Tuple.Create(nErrorCode, naResult);
                }

                long[] naTemp = new long[baUsefulRes.Length / nDataLength];
                for (int i = 0; i < baUsefulRes.Length; i += nDataLength)
                {
                    byte[] baTemp = baUsefulRes.Skip(i * nDataLength).Take(nDataLength).ToArray<byte>();
                    
                    // Since Fatek PLC is Little-endian-based, must reverse it first
                    Array.Reverse(baTemp);
                    
                    if(nDataLength == 2)
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

            return Tuple.Create(nErrorCode, naResult);        // first one is error code, the second rest are useful info.
        }

        public byte setRegValue(PLCInfo.RegType regType, int nRegNumber, long nWriteValue)
        {
            byte nErrorCode = setRegsValue(regType, nRegNumber, 1, new long[]{nWriteValue});
            return nErrorCode;
        }

        public byte setRegsValue(PLCInfo.RegType regType, int nRegNumber, int nWriteNumber, long[] naWriteValues)
        {
            // Example: naWriteValues = [1000, 12000] ==>  "03E8" + "2EE0" = "03E82EE0"
            string strType = regType.ToString();
            string strWriteNumber = nWriteNumber.ToString("D2");
            string strRegNumber = nRegNumber.ToString("D5");
            
            byte nErrorCode = 0;
            long[] naResult = new long[0];
            
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
                    return nErrorCode;
            }

            string strWriteValue = ByteArrayToHexString(naWriteValues, nDataLength);

            string strCmd = strWriteNumber + strType + strRegNumber + strWriteValue;
            byte[] baPLCAsciiRes = SendPLC(PLCInfo.FunctionID.WriteReg, strCmd);
            string strUsefulRes = Encoding.ASCII.GetString(baPLCAsciiRes);

            nErrorCode = byte.Parse(strUsefulRes[0].ToString());

            return nErrorCode;
        }

        public static byte[] RemoveControlCharChksum(byte[] baSource, byte[] baExpectedRes)
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

        public static string RemoveControlCharAndLetter(string strSource, int nOption)
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

        public static byte[] hexStringToHexByteArray(string strSource)
        {
            // Example: "03E8" ==> byte[0x03, 0xE8], "00989680" ==> byte[0x00, 0x98, 0x96, 0x80]

            var baResult = Enumerable.Range(0, strSource.Length / 2)
                             .Select(x => Convert.ToByte(strSource.Substring(x * 2, 2), 16))
                             .ToArray();
            foreach (var item in baResult)
            {
                Console.WriteLine($"baResult: {item}");
            }

            return baResult;

        }

        public static byte[] decStringToDecByteArray(string strSource)
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

        public static byte[] ASCIIArrayToIntArray(byte[] ba)
        {
            // ba: 023031343030303134303030314303
            // strDecValue: 014000140001C
            string strDecValue = Encoding.ASCII.GetString(ba);
            string strUsefulInfo =  RemoveControlCharAndLetter(strDecValue, 1);

            int nDevValue = 0;
            Int32.TryParse(strUsefulInfo, out nDevValue);

            
            Console.WriteLine($"strUsefulInfo: {strUsefulInfo}, nDevValue: {nDevValue}");

            byte[] baDevValue = new byte[strUsefulInfo.Length];

            for (int i = 0; i < strUsefulInfo.Length; i++)
            {
                baDevValue[i] = byte.Parse(strUsefulInfo[i].ToString());
            }

            return baDevValue;
        }

        public static string DecIntToHexString(int nDecValue)
        {
            //Example1: nDecValue = 1000 ==> hexString = "3E8", nDecValue = 10,000,000 ==> hexString = "989680"
            
            // Method1
            //string hexString = nDecValue.ToString("X");      // X or X2 both work

            //Example1: nDecValue = 1000 ==> hexString = "0x3E8", nDecValue = 10,000,000 ==> hexString = "0x989680"
            // Method2
            string hexString = String.Format("0x{0:X}", nDecValue); 
            return hexString;
        }

        public static int HexStringToDecInt(string strHexValue)
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

        public static string ByteArrayToHexString<T>(T[] ba, int nFmtLength = 2)
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

        public static byte[] HexStringToByteArray(String hexString)
        {
            // Example: hexString = "0230313430" ==> hexBytes = [0x02, 0x30, 0x31, 0x34, 0x30]
            int NumberChars = hexString.Length;
            byte[] hexBytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                hexBytes[i / 2] = Convert.ToByte(hexString.Substring(i, 2), 16);
            return hexBytes;
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
        public string strResponse;
        

        public void Set_StationID(int nId)
        {
            strStationID = String.Format("{0:X2}", nId);
        }

        public void Get_PLC_Response(byte[] rsp)
        {

        }

        //// This is check sum (Longitudinal Redundancy Check)
        public byte[] calculateLRC(byte[] bytes)
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
        public Tuple<byte[], byte[]> CombineCommand(string strControlCmd, string strExpectedRes)
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
        
    }

    public static class ErrorMessage
    {
        public static string ToString(this ErrorCode err)
        {
            switch (err)
            {
                case ErrorCode.None:
                    return "None";
                
                default:
                    return "This error message has not been defined yet";
            }
        }
    }
}
