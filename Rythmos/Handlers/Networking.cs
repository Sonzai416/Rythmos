using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using static Rythmos.Handlers.Characters;
using static System.Text.Encoding;

namespace Rythmos.Handlers
{
    internal class Networking
    {
        public static TcpClient Client = new();

        private static Task? Queue_Task;

        public static IPluginLog Log;

        public static IFramework F;

        private static long T = 0;

        private static bool Connecting = false;

        public static string Name;

        private static string ID;

        private static NetworkStream S;

        public static string Progress = "Upload Pack";

        public static bool Downloading = false;

        private static bool Started = false;

        public static string Download_Progress = "";

        private static Task Getter;

        private static CancellationTokenSource Cancel = new();

        public static Configuration C;

        private static bool Reconnect = false;

        private static byte[] Vector = Get_Bytes("67-D5-97-22-57-8F-CC-C3-74-4B-6D-62-DF-81-C8-25");

        private static string IP = null;

        public static string Version = "0.3.0.2";

        public static Task Send(byte[] Data, byte Type)
        {
            return Task.Run(() =>
            {
                if (S is null) return;
                byte[] Output = new byte[Data.Length + 6];
                var Size = Data.Length;
                var E = (byte)(Size % 256);
                Size -= E;
                Size /= 256;
                var D = (byte)(Size % 256);
                Size -= D;
                Size /= 256;
                var C = (byte)(Size % 256);
                Size -= C;
                Size /= 256;
                var B = (byte)(Size % 256);
                Size -= B;
                Size /= 256;
                var A = (byte)(Size % 256);
                Output[0] = A;
                Output[1] = B;
                Output[2] = C;
                Output[3] = D;
                Output[4] = E;
                Output[5] = Type;
                for (var I = 0; I < Data.Length; I++) Output[I + 6] = Data[I];
                S.Write(Output);
            });
        }
        public static Task Get()
        {
            return Task.Run(async () =>
            {
                if (Client.Connected)
                {
                    try
                    {
                        Started = false;
                        var Total = new byte[1024 * 1024 * 1024];
                        var Buffer = new byte[512 * 1024];
                        ulong Offset = 0;
                        while (Client.Connected)
                        {
                            var Received = (ulong)S.Read(Buffer);
                            for (ulong I = 0; I < Received; I++) Total[Offset + I] = Buffer[I];
                            Offset += Received;
                            Offset = Math.Min((ulong)Total.Length - 1, Offset);
                            if (Offset >= 6)
                            {
                                var Size = (ulong)(Total[0] * Math.Pow(256, 4) + Total[1] * Math.Pow(256, 3) + Total[2] * Math.Pow(256, 2) + Total[3] * 256 + Total[4]);
                                if (Total[5] >= 10) Downloading = true;
                                if (Downloading && Size > (ulong)(Total[5] - 10))
                                {
                                    var Downloading_Name = UTF8.GetString(Total.Skip(6).Take(Total[5] - 10).ToArray());
                                    if (!Started) Networking.Send(UTF8.GetBytes(Downloading_Name), 6);
                                    Download_Progress = $" — Downloading {Downloading_Name} (" + (100 * Offset / Size) + "%)";
                                    Characters.Requesting[Downloading_Name] = TimeProvider.System.GetTimestamp();
                                    Started = true;
                                }
                                //if (Downloading) Log.Information("Download: " + (100 * Offset / Size) + "%");
                                while (Offset >= Size + 6) // The data has been totally processed.
                                {
                                    if (Total[5] >= 10)
                                    {
                                        var Downloading_Name = UTF8.GetString(Total.Skip(6).Take(Total[5] - 10).ToArray());
                                        if (!Started) Networking.Send(UTF8.GetBytes(Downloading_Name), 6);
                                        Characters.Requesting[Downloading_Name] = TimeProvider.System.GetTimestamp();
                                        Started = true;
                                    }
                                    var Flag = Total[5];
                                    switch (Flag)
                                    {
                                        case 0:
                                            {
                                                var Message = Total.Take((int)Size + 6).Skip(6).ToArray();
                                                Log.Information($"The server said, \"{UTF8.GetString(Message)}\"");
                                                break;
                                            }
                                        case 1:
                                            {
                                                var Message = Total.Take((int)Size + 6).Skip(6).ToArray();
                                                if (UTF8.GetString(Message) == Name) Progress = "Upload Pack";
                                                break;
                                            }
                                        case 2:
                                            {
                                                var Message = Total.Take((int)Size + 6).Skip(6).ToArray();
                                                Progress = "Uploading " + UTF8.GetString(Message) + "%";
                                                break;
                                            }
                                        case 3:
                                            {
                                                var Message = Total.Take((int)Size + 6).Skip(6).ToArray();
                                                if (Message.Length > 0) foreach (var Part in UTF8.GetString(Message).Split("|"))
                                                    {
                                                        var Split_Part = Part.Split(" ");
                                                        var T = long.Parse(Split_Part[3]);
                                                        var Character = string.Join(" ", Split_Part.Take(3));
                                                        Characters.Server_Time_Mapping[Character] = T;
                                                    }
                                                Clean();
                                                break;
                                            }
                                        case 4:
                                            {
                                                var Message = Total.Take((int)Size + 6).Skip(6).ToArray();
                                                if (Message.Length > 0)
                                                {
                                                    var Split_Message = UTF8.GetString(Message).Split("|");
                                                    var Character = Split_Message[0];
                                                    var Data = string.Join("|", Split_Message.Skip(1));
                                                    Characters.Glamour_Buffer[Character] = Data;
                                                }
                                                break;
                                            }
                                        default:
                                            {
                                                if (Flag >= 10)
                                                {
                                                    var Name_Offset = Flag - 10;
                                                    var File_Name = UTF8.GetString(Total.Skip(6).Take(Name_Offset).ToArray());
                                                    var Character_Name = string.Join(" ", File_Name.Split(" ").Take(3));
                                                    Log.Information(File_Name);
                                                    if (File_Name.Split(" ").Length <= 3)
                                                    {
                                                        Characters.Locked[File_Name] = true;
                                                        var Output = new byte[Size - ((ulong)Name_Offset)];
                                                        Networking.Send(UTF8.GetBytes(File_Name), 5);
                                                        for (ulong I = 0; I < (ulong)Output.Length; I++) Output[I] = Total[I + 6 + ((ulong)Name_Offset)];
                                                        File.WriteAllBytes(Characters.Get_Available(File_Name), Output); // This should be a stream in the future for large file sizes.
                                                        try
                                                        {
                                                            if (Characters.Unpack(File_Name)) F.RunOnTick(() =>
                                                                {
                                                                    Characters.Glamours.Remove(File_Name);
                                                                    Characters.Remove_Collection(Character_Name);
                                                                    if (Characters.ID_Mapping.ContainsKey(File_Name)) Characters.Set_Collection(Characters.ID_Mapping[File_Name]);
                                                                    Characters.File_Time_Mapping[File_Name] = Characters.Server_Time_Mapping[File_Name];
                                                                });
                                                        }
                                                        catch (Exception Error)
                                                        {
                                                            Log.Error("Request Unpacking: " + Error.Message);
                                                        }
                                                        Characters.Locked[File_Name] = false;
                                                        Downloading = false;
                                                        Started = false;
                                                        Download_Progress = "";
                                                    }
                                                    else
                                                    {
                                                        if (!Directory.Exists(Rythmos_Path + "\\Parts\\" + Character_Name)) Directory.CreateDirectory(Rythmos_Path + "\\Parts\\" + Character_Name);
                                                        Log.Information(File_Name);
                                                        Characters.Locked[Character_Name] = true;
                                                        var Part = int.Parse(File_Name.Split(" ")[3]);
                                                        var End = false;
                                                        var Skip = File_Name.EndsWith("Skip");
                                                        if (Part == -1)
                                                        {
                                                            File_Name = "Configuration.json";
                                                            try
                                                            {
                                                                if (Directory.Exists(Rythmos_Path + "\\Mods\\" + Character_Name)) Directory.Delete(Rythmos_Path + "\\Mods\\" + Character_Name, true);
                                                            }
                                                            catch (Exception Error)
                                                            {
                                                                Log.Error("Request: " + Error.Message);
                                                            }
                                                            try
                                                            {
                                                                if (File.Exists(Rythmos_Path + "\\Compressed\\" + Character_Name + ".zip")) File.Delete(Rythmos_Path + "\\Compressed\\" + Character_Name + ".zip");
                                                            }
                                                            catch (Exception Error)
                                                            {
                                                                Log.Error("Request: " + Error.Message);
                                                            }
                                                            Directory.CreateDirectory(Rythmos_Path + $"\\Mods\\{Character_Name}");
                                                        }
                                                        else
                                                        {
                                                            try
                                                            {
                                                                var Order = JsonConvert.DeserializeObject<Mod_Configuration>(File.ReadAllText(Rythmos_Path + $"\\Parts\\{Character_Name}\\Configuration.json")).Order;
                                                                File_Name = Order[Part] + ".zip";
                                                                Log.Information("Part End: " + End);
                                                                End = Part + 1 == Order.Count;
                                                            }
                                                            catch (Exception Error)
                                                            {
                                                                if (File.Exists(Rythmos_Path + $"\\Parts\\{Character_Name}\\Configuration.json")) File.Delete(Rythmos_Path + $"\\Parts\\{Character_Name}\\Configuration.json");
                                                                Log.Error($"An error occurred while decoding part {Part} of {Character_Name}: {Error.Message}");
                                                                File_Name = null;
                                                            }
                                                        }
                                                        if (File_Name != null)
                                                        {
                                                            File_Name = Rythmos_Path + $"\\Parts\\{Character_Name}\\" + File_Name;
                                                            if (!Skip)
                                                            {
                                                                var Output = new byte[Size - ((ulong)Name_Offset)];
                                                                for (ulong I = 0; I < (ulong)Output.Length; I++) Output[I] = Total[I + 6 + ((ulong)Name_Offset)];
                                                                File.WriteAllBytes(File_Name, Output);
                                                            }
                                                            Log.Information((Skip ? "Skipped" : "Received") + $" {Character_Name} " + Part);
                                                            if (Part == -1) if (JsonConvert.DeserializeObject<Mod_Configuration>(File.ReadAllText(File_Name)).Mods.Keys.Count == 0) End = true;
                                                            var Problem = false;
                                                            if (Part > -1)
                                                            {
                                                                try
                                                                {
                                                                    Log.Information($"Unzipping {Rythmos_Path + "\\Mods\\" + Character_Name + "\\" + File_Name.Split("\\")[^1].Split(".zip")[0]}...");
                                                                    ZipFile.ExtractToDirectory(File_Name, Rythmos_Path + "\\Mods\\" + Character_Name + "\\" + File_Name.Split("\\")[^1].Split(".zip")[0], true);
                                                                }
                                                                catch (Exception Error)
                                                                {
                                                                    try
                                                                    {
                                                                        File.Delete(File_Name);
                                                                    }
                                                                    catch (Exception Deletion_Error)
                                                                    {
                                                                        Log.Error("Request Unzipping Deletion: " + Deletion_Error.Message);
                                                                    }
                                                                    Log.Error("Request Unzipping: " + Error.Message);
                                                                    Part -= 1;
                                                                    Problem = true;
                                                                }
                                                            }
                                                            if (End && !Problem)
                                                            {
                                                                var Configuration = Rythmos_Path + $"\\Parts\\{Character_Name}\\Configuration.json";
                                                                File.Copy(Configuration, Rythmos_Path + $"\\Mods\\{Character_Name}\\Configuration.json");
                                                                Networking.Send(UTF8.GetBytes(Character_Name), 5);
                                                                Characters.Locked[Character_Name] = false;
                                                                F.RunOnTick(() =>
                                                                {
                                                                    try
                                                                    {
                                                                        Characters.Glamours.Remove(Character_Name);
                                                                        Characters.Remove_Collection(Character_Name);
                                                                        if (Characters.ID_Mapping.ContainsKey(Character_Name)) Characters.Set_Collection(Characters.ID_Mapping[Character_Name]);
                                                                        Characters.File_Time_Mapping[Character_Name] = new DateTimeOffset(File.GetLastWriteTimeUtc(Rythmos_Path + $"\\Mods\\{Character_Name}\\Configuration.json")).ToUnixTimeMilliseconds();
                                                                        //Characters.File_Time_Mapping[Character_Name] = Characters.Server_Time_Mapping[Character_Name];
                                                                    }
                                                                    catch (Exception Error)
                                                                    {
                                                                        Log.Error("Request Unpacking: " + Error.Message);
                                                                    }
                                                                });
                                                                Downloading = false;
                                                                Started = false;
                                                                Download_Progress = "";
                                                            }
                                                            else
                                                            {
                                                                var Next_Name = JsonConvert.DeserializeObject<Mod_Configuration>(File.ReadAllText(Rythmos_Path + $"\\Parts\\{Character_Name}\\Configuration.json")).Order[Part + 1];
                                                                var Next = Rythmos_Path + $"\\Parts\\{Character_Name}\\{Next_Name}.zip";
                                                                Log.Information($"Requesting {Next_Name} for {Character_Name}.");
                                                                Networking.Send(UTF8.GetBytes(Character_Name + " " + (Part + 1) + " " + (File.Exists(Next) ? new DateTimeOffset(File.GetLastWriteTimeUtc(Next)).ToUnixTimeMilliseconds() : 0)), 2);
                                                            }
                                                        }
                                                        else
                                                        {
                                                            Characters.Locked[Character_Name] = false;
                                                            Downloading = false;
                                                            Started = false;
                                                            Download_Progress = "";
                                                        }
                                                    }
                                                }
                                                else Log.Warning($"{Flag} is an unknown flag.");
                                                break;
                                            }
                                    }
                                    Offset -= Size + 6;
                                    for (var I = (ulong)0; I < Offset; I++) Total[I] = Total[I + Size + 6];
                                    Size = (ulong)(Total[0] * Math.Pow(256, 4) + Total[1] * Math.Pow(256, 3) + Total[2] * Math.Pow(256, 2) + Total[3] * 256 + Total[4]);
                                }
                            }
                        }
                    }
                    catch (Exception Error)
                    {
                        Downloading = false;
                        Started = false;
                        Reconnect = true;
                        Client?.Close();
                        Log.Error("Request: " + Error.Message);
                    }
                }
            }, Cancel.Token);
        }

        private static byte[] Get_Bytes(string Input)
        {
            var Buffer = Input.Split("-");
            byte[] Data = new byte[Buffer.Length];
            for (var I = 0; I < Buffer.Length; I++) Data[I] = byte.Parse(Buffer[I], System.Globalization.NumberStyles.HexNumber);
            return Data;
        }
        private async static Task<byte[]> Encrypt(string Data, string Key)
        {
            using Aes A = Aes.Create();
            A.Key = Rfc2898DeriveBytes.Pbkdf2(UTF8.GetBytes(Key), Array.Empty<byte>(), 1000, HashAlgorithmName.SHA512, 16);
            A.IV = Vector;
            using MemoryStream Output = new();
            using CryptoStream CS = new(Output, A.CreateEncryptor(), CryptoStreamMode.Write);
            await CS.WriteAsync(UTF8.GetBytes(Data));
            await CS.FlushFinalBlockAsync();
            return Output.ToArray();
        }
        private async static Task<string> Decrypt(byte[] Data, string Key)
        {
            using Aes A = Aes.Create();
            A.Key = Rfc2898DeriveBytes.Pbkdf2(UTF8.GetBytes(Key), Array.Empty<byte>(), 1000, HashAlgorithmName.SHA512, 16);
            A.IV = Vector;
            using MemoryStream I = new(Data);
            using CryptoStream CS = new(I, A.CreateDecryptor(), CryptoStreamMode.Read);
            using MemoryStream Output = new();
            await CS.CopyToAsync(Output);
            return UTF8.GetString(Output.ToArray());
        }

        public async static Task Connect()
        {
            if (!Connecting && Characters.Objects.LocalPlayer is not null)
            {
                Connecting = true;
                //Log.Information("Connecting!");
                try
                {
                    Progress = "Upload Pack";
                    Downloading = false;
                    Cancel.Cancel();
                    Cancel.Dispose();
                    Getter = null;
                    Cancel = new();
                    Client?.Dispose();
                    Client = new TcpClient();
                    Client.ReceiveTimeout = 0;
                    Client.SendTimeout = 120000;
                    var Token = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using (var Web = new HttpClient()) IP = await Decrypt(Get_Bytes(await Web.GetStringAsync(await Decrypt(Get_Bytes("0E-F0-04-57-9B-AB-A9-A5-7D-8B-07-C1-64-40-0C-93-C9-04-75-D0-C6-C9-51-A1-DD-13-1A-46-51-49-60-FC-12-ED-68-11-66-C9-9C-BB-09-F9-2E-F6-B5-8B-86-7E-6A-AF-04-1C-2A-E5-CF-85-66-CB-8C-E2-CF-07-DB-4D-C7-A1-6E-39-75-25-6A-2F-1E-23-16-E1-B9-E8-C0-00"), "Rythmos"))), "Rythmos");
                    if (IP is null) return;
                    if (IP.Length == 0) return;
                    await Client.ConnectAsync(IPAddress.Parse(IP), 64141, Token.Token);
                    S = Client.GetStream();
                    Queue.Start(S);
                    if (Name.Length > 0)
                    {
                        Networking.Send(UTF8.GetBytes(Name + " " + ID + " " + Version), 0);
                        F.RunOnTick(() => Characters.Update_Glamour(Characters.Objects.LocalPlayer.Address));
                    }
                    Getter = Get();
                    Reconnect = false;
                }
                catch (Exception Error)
                {
                    //Log.Error("Connect: " + Error.Message);
                }
                Connecting = false;
            }
        }
        public static void Update(IFramework F)
        {
            var New_T = TimeProvider.System.GetTimestamp();
            if (New_T - T > 10000000 * (Downloading ? 10 : 1))
            {
                try
                {
                    if (!Customize.Ready) Customize.Setup(Customize.Interface);
                    if (!Glamour.Ready) Glamour.Setup(Glamour.Interface);
                    if (Characters.Objects.LocalPlayer is not null)
                    {
                        var Current_Name = Characters.Get_Name(Characters.Objects.LocalPlayer.ObjectIndex);
                        if (Name != Current_Name)
                        {
                            Reconnect = true;
                            Name = Current_Name;
                            if (Client != null)
                            {
                                Client.Close();
                                Client = new();
                            }
                        }
                        if (!Client.Connected || Reconnect)
                        {
                            ID = C.Player.Length == 0 ? Name : C.Player;
                            if (ID != C.Player)
                            {
                                C.Player = ID;
                                C.Save();
                            }
                            //Log.Information("Trying to connect!");
                            Connect();
                        }
                        else Networking.Send(Array.Empty<byte>(), 3);
                        T = New_T;
                    }
                }
                catch (Exception Error)
                {
                    //Log.Error("Update Connecting: " + Error.Message);
                }
            }
        }
        public static void Dispose()
        {
            Client.Dispose();
            Cancel.Cancel();
            Cancel.Dispose();
            Getter = null;
        }

    }


}
