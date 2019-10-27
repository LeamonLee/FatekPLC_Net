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
                Console.WriteLine($"command sent: {ByteArrayToString(baSendCom)}");         // command sent: 0230313430433703

                int nRcvBytesLength = SocketPLC.Receive(baRcvBuffer, SocketFlags.None);
                Console.WriteLine($"nRcvBytesLength: {nRcvBytesLength}");
                byte[] baPLCRes = new byte[nRcvBytesLength];
                Array.Copy(baRcvBuffer, baPLCRes, nRcvBytesLength);
                Console.WriteLine($"baPLCRes: {ByteArrayToString(baPLCRes)}");              // baPLCRes: 023031343030303134303030314303


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

        public Tuple<byte, long[]> getRegsState(PLCInfo.RegType regType, int nRegNumber, int nReadNumber)
        {
            string strType = regType.ToString();
            string strReadNumber = nReadNumber.ToString("D2");
            string strRegNumber = nRegNumber.ToString("D5");
            string strCmd = strReadNumber + strType + strRegNumber;
            byte[] baPLCAsciiRes = SendPLC(PLCInfo.FunctionID.ReadReg, strCmd);
            string strUsefulRes = Encoding.ASCII.GetString(baPLCAsciiRes);

            Console.WriteLine("strUsefulRes: {0}, {1}", strUsefulRes, strUsefulRes.Length);

            // Example: R0 = 1000, strUsefulRes= 003E8
            byte nErrorCode = byte.Parse(strUsefulRes[0].ToString());
            Console.WriteLine($"nErrorCode: {nErrorCode}");

            byte[] baUsefulRes = new byte[0];
            long[] naResult = new long[0];
            if (nErrorCode == 0)
            {
                baUsefulRes = hexStringToHexByteArray(strUsefulRes.Substring(1));
                foreach (var item in baUsefulRes)
                {
                    Console.WriteLine($"baUsefulRes: {item}");
                }

                long[] naTemp = new long[baUsefulRes.Length / 2];
                for (int i = 0; i < baUsefulRes.Length; i+=2)
                {
                    byte[] baTemp = new byte[] { baUsefulRes[0], baUsefulRes[1] };
                    // Due to Little endian, must reverse it first
                    Array.Reverse(baTemp);
                    UInt16 nRegValue = BitConverter.ToUInt16(baTemp, 0);
                    naTemp[i] = nRegValue;
                }
                naResult = naTemp;
                
            }

            return Tuple.Create(nErrorCode, naResult);        // first one is error code, the second rest are useful info.
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
            // Example: "03E8"  ==> byte[0x03, 0xE8]
            return Enumerable.Range(0, strSource.Length / 2)
                             .Select(x => Convert.ToByte(strSource.Substring(x * 2, 2), 16))
                             .ToArray();

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

        public static string intToHexString(int nValue)
        {
            // Method1
            //string hexString = nValue.ToString("X");

            // Method2
            string hexString = String.Format("0x{0:X}", nValue);
            return hexString;
        }

        public static int HexStringToInt(string strHexValue)
        {
            // strip the leading 0x
            if (strHexValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                strHexValue = strHexValue.Substring(2);
            }

            // Method1
            int nIntValue = Convert.ToInt32(strHexValue, 16);  // Back to int again.

            // Method2
            //int nIntValue = Int32.Parse(strValue, System.Globalization.NumberStyles.HexNumber);

            return nIntValue;
        }

        public static string ByteArrayToString(byte[] ba, bool isDecimal = false)
        {
            //string hex = BitConverter.ToString(data).Replace("-", string.Empty);

            StringBuilder hex = new StringBuilder();
            foreach (byte b in ba)
            {
                if(isDecimal == false)
                    hex.AppendFormat("{0:x2}", b);
                else
                    hex.AppendFormat("{0:D}", b);
                
            }

            return hex.ToString();
        }

        public static byte[] StringToByteArray(String hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
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
}
