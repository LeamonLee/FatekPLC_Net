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
            bool isConnected = fatekPLC.ConnectPLC();
            Console.WriteLine($"isConnected: {isConnected}");

            var result = fatekPLC.getPLCInfo();
            Console.WriteLine("ErrorCode: {0}", result.Item1);

            foreach (var item in result.Item2)
            {
                Console.WriteLine("item: {0}", item);
            }

            var M0 = fatekPLC.getCoilState(PLCInfo.CoilType.M, 0);
            Console.WriteLine("ErrorCode: {0}, M0: {1}", M0.Item1, M0.Item2);


            var R0 = fatekPLC.getRegsValue(PLCInfo.RegType.R, 0, 1);
            Console.WriteLine("ErrorCode: {0}", R0.Item1);

            foreach (var item in R0.Item2)
            {
                Console.WriteLine("item: {0}", item);
            }

            var nErrorCode = fatekPLC.setRegsValue(PLCInfo.RegType.DR, 2, 3, new long[] { 1000, 2000, 3000 });
            Console.WriteLine("nErrorCode: {0}", nErrorCode);

            var DR2 = fatekPLC.getRegValue(PLCInfo.RegType.DR, 2);
            Console.WriteLine("ErrorCode: {0}, DR2: {1}", DR2.Item1, DR2.Item2);



            Console.ReadLine();
        }

    }
}
