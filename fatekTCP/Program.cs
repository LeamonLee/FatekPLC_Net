using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace fatekTCP
{
	class Program
	{
		static void Main(string[] args)
		{
			Fatek_Net fatekPLC = new Fatek_Net(1, "192.168.2.3", 500);

			// Connect to PLC
			bool isConnected = fatekPLC.ConnectPLC();
			Console.WriteLine($"isConnected: {isConnected}, fatekPLC.isConnected: {fatekPLC.isConnected}");

			// Connect to PLC asynchronously
			//fatekPLC.ConnectPLCAsync(connectPLC_Callback);

			if (fatekPLC.isConnected)
			{
				// ========================================

				// Read Coil status Ex: M0, X0, Y0...etc.
				var M0 = fatekPLC.getCoilState(PLCInfo.CoilType.M, 0);
				Console.WriteLine("ErrorCode: {0}, M0: {1}", M0.Item1, M0.Item2);

				// Read Coil status asynchronously.
				fatekPLC.getCoilStateAsync(readM0_Callback, PLCInfo.CoilType.M, 0);

				// ========================================

				// Read single register 
				var R10 = fatekPLC.getRegValue(PLCInfo.RegType.R, 10);
				Console.WriteLine("ErrorCode: {0}, Value: {1}", R10.Item1, R10.Item2);

				// Read register value asynchronously.
				fatekPLC.getRegValueAsync(readR10_Callback, PLCInfo.RegType.R, 10);

				// ========================================

				// Read  multiple registers in a row. Ex: R0~R4
				var results = fatekPLC.getRegsValue(PLCInfo.RegType.R, 0, 5);
				Console.WriteLine("ErrorCode: {0}", results.Item1);
				for (int i = 0; i < results.Item2.Length; i++)
				{
					Console.WriteLine("R{0} value: {1}", i, results.Item2[i]);
				}

				// ========================================

				// Set single register
				var nErrorCode = fatekPLC.setRegValue(PLCInfo.RegType.R, 20, 5000);
				Console.WriteLine("nErrorCode: {0}", nErrorCode);

				// Set single register asynchronously
				fatekPLC.setRegValueAsync(setR20_Callback, PLCInfo.RegType.R, 20, 6000);

				// ========================================

				// Set multiple registers(DR2, DR4, DR6) to 1000, 2000, 3000 respectively.
				nErrorCode = fatekPLC.setRegsValue(PLCInfo.RegType.DR, 2, 3, new long[] { 1000, 2000, 3000 });
				Console.WriteLine("nErrorCode: {0}", nErrorCode);

				// Set multiple registers asynchronously
				fatekPLC.setRegsValueAsync(setMultiRegs_Callback, PLCInfo.RegType.DR, 2, 3, new long[] { 2000, 4000, 6000 });
			}

			Console.ReadLine();
		}

		private static void connectPLC_Callback(bool isConnected)
		{
			Console.WriteLine("connectPLC_Callback isConnected: {0}", isConnected);

		}

		private static void setMultiRegs_Callback(ErrorCode err)
		{
			Console.WriteLine("setMultiRegs_Callback Error: {0}", err.ToString());
		}

		private static void setR20_Callback(ErrorCode err)
		{
			Console.WriteLine("setR20_Callback Error: {0}", err.ToString());
		}

		private static void readR10_Callback(ErrorCode err, long regValue)
		{
			if ((int)err != 0)
			{
				Console.WriteLine("readR10_Callback Error: {0}", err.ToString());
			}

			Console.WriteLine("Callback function R10: {0}", regValue);
		}

		private static void readM0_Callback(ErrorCode err, byte coilValue)
		{
			if ((int)err != 0)
			{
				Console.WriteLine("readM0_Callback Error: {0}", err.ToString());
			}

			Console.WriteLine("Callback function M0: {0}", coilValue);
		}
	}
}
