using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcService.Web.Protos;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace GrpcClient
{
    class Program
    {
        private static string _token;
        private static DateTime _expiration = DateTime.MinValue;
        static List<EmployeeRequest> employeeRequests = new List<EmployeeRequest>
            {
                new EmployeeRequest
                {
                     Employee=new Employee
                     {
                          Id=10,
                           No=111,
                            FirstName="guo",
                            LastName="degang",
                      Status=EmployeeStatus.Normal,
                 MonthSalary=new MonthSalary
                    {
                         Basic=123,
                          Bonus=10000
                    },
                 LastModified=Timestamp.FromDateTime(DateTime.UtcNow)
                     }
                },
                new EmployeeRequest
                {
                     Employee=new Employee
                     {
                          Id=20,
                           No=222,
                            FirstName="yu",
                            LastName="qian",
                      Status=EmployeeStatus.Onvacation,
                 MonthSalary=new MonthSalary
                    {
                         Basic=222,
                          Bonus=100066660
                    },
                 LastModified=Timestamp.FromDateTime(DateTime.UtcNow)
                     },
                },
                new EmployeeRequest
                {
                     Employee=new Employee
                     {
                          Id=30,
                           No=333,
                            FirstName="wei",
                            LastName="lihang",
                      Status=EmployeeStatus.Resigned,
                 MonthSalary=new MonthSalary
                    {
                         Basic=(float)22.3,
                          Bonus=1234
                    },
                 LastModified=Timestamp.FromDateTime(DateTime.UtcNow)
                     },
                },
            };
        static async Task Main(string[] args)
        {
            //使用logger 记录grpc 通信
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .CreateLogger();
            Log.Information("Client starting....");
            using var channel = GrpcChannel.ForAddress("https://localhost:5001",
                new GrpcChannelOptions
                {
                    LoggerFactory = new SerilogLoggerFactory()
                });
            var client = new EmployeeService.EmployeeServiceClient(channel);
            //client 可以携带metadata headers.
            //var option = args[0];
            var option = "1";
            switch (option)
            {
                case "1":
                    await GetByNoAsync(client);
                    break;
                case "2":
                    await GetAllAsync(client);
                    break;
                case "3":
                    await AddPhotoAsync(client);
                    break;
                case "4":
                    await SaveAllAsync(client);
                    break;
                default:
                    Console.WriteLine("No suitable option.");
                    break;
            }

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
            Log.CloseAndFlush();
        }

        /// <summary>
        /// 一元消息
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public static async Task GetByNoAsync(EmployeeService.EmployeeServiceClient client)
        {
            if (!NeedToken() || await GetTokenAsync(client))
            {
                try
                {
                    var headers = new Metadata()
            {
                {"Authorization",$"Bearer {_token}" },
                {"username","kael.tian" },
                {"age","18" },
            };
                    headers.Add(new Metadata.Entry("level", "GL8"));
                    var response = await client.GetByNoAsync(new GetByNoRequest
                    {
                        No = 1994
                    }, headers);
                    Console.WriteLine($"Response messages: {response}");
                }
                catch (RpcException e)
                {
                    if (e.StatusCode == StatusCode.PermissionDenied)
                    {
                        Log.Logger.Error($"{e.Trailers}");
                        foreach (var pair in e.Trailers)
                        {
                            Log.Logger.Error($"Key:{pair.Key},Value:{pair.Value}");
                        }
                    }
                    Log.Logger.Error(e.Message);
                }
            }
        }

        private static async Task<bool> GetTokenAsync(EmployeeService.EmployeeServiceClient client)
        {
            var request = new TokenRequest()
            {
                Username = "admin",
                Password = "1qaz2wsxE"
            };
            var response = await client.CreateTokenAsync(request);
            if (response.Success)
            {
                _token = response.Token;
                _expiration = response.Expiration.ToDateTime();
                return true;
            }
            return false;
        }

        /// <summary>
        /// server streaming
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public static async Task GetAllAsync(EmployeeService.EmployeeServiceClient client)
        {
            using var call = client.GetAll(new GetAllRequest { });
            var responseStream = call.ResponseStream;
            while (await responseStream.MoveNext())
            {
                Console.WriteLine(responseStream.Current.Employee);
            }
        }

        /// <summary>
        /// client streaming
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public static async Task AddPhotoAsync(EmployeeService.EmployeeServiceClient client)
        {
            var md = new Metadata()
            {
                {"username","kael.tian" },
                {"age","18" },
            };
            FileStream fs = File.OpenRead("JOJO.jpg");
            //可以先传metadata.
            using var cal = client.AddPhoto(md);
            var stream = cal.RequestStream;
            while (true)
            {
                byte[] buffer = new byte[1024 * 2];
                int numRead = await fs.ReadAsync(buffer, 0, buffer.Length);
                if (numRead == 0)
                {
                    break;
                }

                if (numRead < buffer.Length)
                {
                    Array.Resize(ref buffer, numRead);
                }

                await stream.WriteAsync(new AddPhotoRequest
                {
                    Data = ByteString.CopyFrom(buffer)
                });
            }

            //告诉server端数据已全部传输完毕.
            await stream.CompleteAsync();

            //获取响应
            var res = await cal.ResponseAsync;
            var isOk = res.IsOk ? "Success" : "Failed";
            Console.WriteLine($"Response:{isOk}");
        }

        /// <summary>
        /// server streaming and client streaming
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public static async Task SaveAllAsync(EmployeeService.EmployeeServiceClient client)
        {
            //using 是c#8 新的语法糖,当执行完当前方法后,cal对象会被释放掉
            var md = new Metadata()
            {
                {"client","10.2.39.38" },
                {"message","I am client" },
            };
            using var cal = client.SaveAll(md);
            var stream = cal.RequestStream;
            var serverStream = cal.ResponseStream;
            var responseTask = Task.Run(async () =>
              {
                  var serverMD = await cal.ResponseHeadersAsync;
                  foreach (var pair in serverMD)
                  {
                      Console.WriteLine($"Server key:{pair.Key},value:{pair.Value}");
                  }
                  while (await serverStream.MoveNext())
                  {
                      var serverData = serverStream.Current.Employee;
                      Console.WriteLine($"Client received data:{serverData}");
                  }
              });
            foreach (var employeeRequest in employeeRequests)
            {
                await stream.WriteAsync(employeeRequest);
            }
            //告诉server端数据已全部传输完毕.
            await stream.CompleteAsync();
            //先后顺序有要求,要client先发completed,之后才能调用启动接收server response的task,不然server端不知道client
            //何时终止,不会继续往下面执行
            await responseTask;
        }

        private static bool NeedToken() => string.IsNullOrEmpty(_token) || _expiration > DateTime.UtcNow;
    }
}
