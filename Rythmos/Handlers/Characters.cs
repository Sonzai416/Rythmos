using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using InteropGenerator.Runtime;
using Lumina.Excel.Sheets;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Penumbra.Api;
using Penumbra.Api.Api;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;
using Rythmos.Windows;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
namespace Rythmos.Handlers
{
    internal class Characters
    {
        public static IObjectTable Objects;

        public static IClientState Client;

        public static Plugin P;

        private static Dictionary<string, Guid> Collection_Mapping = new();

        private static long T = 0;

        private static long Background_T = 0;

        private static long Request_T = 0;

        public class Mod_Entry
        {
            public string Item1;
            public int Item2;
            public Dictionary<string, List<string>> Item3;
            public Mod_Entry(string I1, int I2, Dictionary<string, List<string>> I3)
            {
                Item1 = I1;
                Item2 = I2;
                Item3 = I3 ?? new Dictionary<string, List<string>> { };
            }

        }
        public class Mod_Configuration
        {

            public string Meta { get; set; } = "";

            public string Bones { get; set; } = "";

            public string Glamour { get; set; } = "";

            public Dictionary<string, Mod_Entry> Mods { get; set; } = new();

            public List<string> Order { get; set; } = new();
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
            [DefaultValue(true)]
            public bool External { get; set; } = true;

            public Mod_Configuration(string Bones, string Glamour, string Meta, Dictionary<string, Mod_Entry> Mods, List<string>? Order, bool External)
            {
                this.Bones = Bones;
                this.Glamour = Glamour;
                this.Meta = Meta;
                this.Mods = Mods ?? new Dictionary<string, Mod_Entry>();
                this.Order = Order ?? this.Mods.Keys.ToList();
                this.External = External;
            }
        }

        private static int Threshold = 4 * 1024 * 1024;

        private static Dictionary<string, Mod_Configuration> Mods = new();

        public static Dictionary<string, ushort> ID_Mapping = new();

        public static IPluginLog Log;

        public static IPartyList Party;

        private static CreateTemporaryCollection Collection_Creator;

        private static RedrawObject Redraw;

        private static RemoveTemporaryMod Temporary_Mod_Remover;

        private static AddTemporaryMod Temporary_Mod_Adder;

        private static AssignTemporaryCollection Collection_Assigner;

        private static DeleteTemporaryCollection Collection_Remover;

        private static GetCollectionForObject Collection_Getter;

        private static GetAllModSettings Settings_Getter;

        private static GetMetaManipulations Get_Meta;

        public static GetGameObjectResourcePaths Get_Resources;

        public static GetPlayerResourceTrees Get_Trees;

        public static ResolvePaths Resolver;

        public static string Penumbra_Path = "";

        public static string Rythmos_Path = "";

        public static List<string> Entities = new();

        private static Dictionary<string, ushort> Pets = new();

        private static Dictionary<string, ushort> Minions = new();

        public static Dictionary<string, string> Glamours = new();

        public static HashSet<string> Recustomize = new();

        private static List<string> Types = ["common\\", "bgcommon\\", "bg\\", "cut\\", "chara\\", "shader\\", "ui\\", "sound\\", "vfx\\", "ui_script\\", "exd\\", "game_script\\", "music\\", "sqpack_test\\", "debug\\"];

        public static IDataManager Data_Manager;

        public static Guid Default = new();

        public static Dictionary<string, long> Requesting = new();

        public static Dictionary<string, string> Glamour_Buffer = new();

        public static Dictionary<string, long> File_Time_Mapping = new();

        public static Dictionary<string, long> Server_Time_Mapping = new();

        public static Dictionary<string, bool> Locked = new();

        public static EventSubscriber<nint, int> Redraw_Handler;

        public static ConvertTextureFile Converter;

        private static bool Busy = false;

        private static Dictionary<string, DateTime> Wait = new();

        private static Dictionary<string, DateTime> Collection_Wait = new();

        private static Dictionary<string, bool> Individually_Set = new();

        private static bool Available = false;

        unsafe private static Task<bool> Check_Draw(int Object_Index, System.Action Callback) => Networking.F.RunOnTick(() =>
        {
            var Visible = ((BattleChara*)Objects[Object_Index].Address)->DrawObject->IsVisible;
            if (Visible)
            {
                Callback();
                Log.Information("Done!");
            }
            return Visible;
        });

        public static void Setup(IDalamudPluginInterface I, IChatGui Chat)
        {
            try
            {
                Collection_Creator = new CreateTemporaryCollection(I);
                Collection_Assigner = new AssignTemporaryCollection(I);
                Redraw = new RedrawObject(I);
                Temporary_Mod_Adder = new AddTemporaryMod(I);
                Temporary_Mod_Remover = new RemoveTemporaryMod(I);
                Collection_Getter = new GetCollectionForObject(I);
                Settings_Getter = new GetAllModSettings(I);
                Get_Meta = new GetMetaManipulations(I);
                Collection_Remover = new DeleteTemporaryCollection(I);
                Get_Resources = new GetGameObjectResourcePaths(I);
                Get_Trees = new GetPlayerResourceTrees(I);
                Resolver = new ResolvePaths(I);
                Penumbra_Path = new GetModDirectory(I).Invoke();
                Converter = new ConvertTextureFile(I);
                Redraw_Handler = GameObjectRedrawn.Subscriber(I, async (nint A, int Object_Index) =>
                {
                    if (Networking.C.Sync_Penumbra && Object_Index == 0 && !Busy)
                    {
                        Busy = true;
                        var Invisible = true;
                        while (Invisible)
                        {
                            Invisible = !(await Check_Draw(Object_Index, () => P.Packing(Networking.Name, 2, true).ContinueWith(_ => Busy = false)));
                            await Task.Delay(250);
                        }
                    }
                });
            }
            catch (Exception Error)
            {
                Chat.PrintError("[Rythmos] Please update Penumbra!");
                Log.Error(Error.Message);
            }
        }

        private static string Archive_Name(string Name, int Index) => Rythmos_Path + "\\Compressed\\" + (Name + " " + Index.ToString()).Replace(" 0", " ").Trim() + ".zip";

        public static string Get_Newest(string Name)
        {
            var Last_Time = DateTime.MinValue;
            if (Wait.ContainsKey(Name)) Last_Time = Wait[Name];
            var Now = DateTime.Now;
            if (Now.Subtract(Last_Time) < TimeSpan.FromSeconds(1)) return null;
            Wait[Name] = Now;
            var Compressed_Path = Rythmos_Path + "\\Compressed";
            var Candidates = Directory.GetFiles(Compressed_Path).ToList().FindAll(X => X.Split(Compressed_Path)[^1].StartsWith("\\" + Name));
            if (Candidates.Count == 0) return null;
            var True = Candidates.MaxBy(File.GetLastWriteTimeUtc);
            foreach (var Candidate in Candidates) if (Candidate != True)
                {
                    try
                    {
                        File.Delete(Candidate);
                    }
                    catch (IOException E)
                    {
                        Log.Error("An error occurred while deleting an old archive: " + E.Message);
                    }
                }
            Log.Information($"The newest version of {Name} is {True}.");
            return True;
        }

        public static string Get_Available(string Name)
        {
            var Index = -1;
            var Locked = false;
            do
            {
                Index++;
                try
                {
                    if (!File.Exists(Archive_Name(Name, Index))) break;
                    using (FileStream S = new FileInfo(Archive_Name(Name, Index)).Open(FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        S.Close();
                    }
                    Locked = false;
                }
                catch (IOException)
                {
                    Locked = true;
                }
            } while (Locked);
            return Archive_Name(Name, Index);
        }

        public static string Get_Name(ushort ID)
        {
            if (ID < Objects.Length)
            {
                var O = Objects[ID];
                if (O == null) return "";
                if (O is IPlayerCharacter) return O.Name.TextValue + " " + ((IPlayerCharacter)O).HomeWorld.Value.Name.ToString();

                return O.Name.TextValue;
            }
            else return "";
        }

        public static bool Create_Collection(string Name)
        {
            var Newest_Version = Get_Newest(Name);
            var Configuration_Exists = File.Exists(Rythmos_Path + $"\\Mods\\{Name}\\Configuration.json");
            if (Mods.ContainsKey(Name) ? true : ((Configuration_Exists || Newest_Version != null) && (Locked.ContainsKey(Name) ? !Locked[Name] : true)))
            {
                File_Time_Mapping[Name] = 0;
                if (Newest_Version != null ? (Configuration_Exists ? (File.GetLastWriteTimeUtc(Rythmos_Path + $"\\Mods\\{Name}\\Configuration.json") < File.GetLastWriteTimeUtc(Newest_Version)) : true) : false) Unpack(Name);
                if (Configuration_Exists)
                {
                    if (!Load(Name)) return false;
                    File_Time_Mapping[Name] = new DateTimeOffset(File.GetLastWriteTimeUtc(Rythmos_Path + $"\\Mods\\{Name}\\Configuration.json")).ToUnixTimeMilliseconds();
                    Log.Information($"Creating the collection of {Name}!");
                    if (Collection_Mapping.ContainsKey(Name)) Collection_Remover.Invoke(Collection_Mapping[Name]);
                    Collection_Creator.Invoke(Name, Name, out var Collection_ID);
                    Collection_Mapping.Add(Name, Collection_ID);
                    Prepare(Name);
                    return true;
                }
                else return false;
            }
            else
            {
                File_Time_Mapping[Name] = 0;
                return false;
            }
        }

        public static bool Set_Collection(ushort ID)
        {
            var Name = Get_Name(ID);

            if (!Individually_Set.ContainsKey(Name)) Individually_Set[Name] = Collection_Getter.Invoke(ID).IndividualSet;
            if ((!Collection_Mapping.ContainsKey(Name) || !ID_Mapping.ContainsKey(Name)) && !Individually_Set[Name])
            {
                if (!ID_Mapping.ContainsKey(Name)) ID_Mapping.Add(Name, ID);
                if (!Collection_Mapping.ContainsKey(Name)) Create_Collection(Name);
                if (Collection_Mapping.ContainsKey(Name))
                {
                    Log.Information($"Assignment of {Name}: " + Collection_Assigner.Invoke(Collection_Mapping[Name], (int)ID_Mapping[Name]).ToString(), true);
                    Enable(Name);
                    return true;
                }
            }
            else if (Recustomize.Contains(Name) && !Individually_Set[Name])
            {
                Log.Information($"Reassignment of {Name}: " + Collection_Assigner.Invoke(Collection_Mapping[Name], (int)ID_Mapping[Name]).ToString(), true);
                return true;
            }
            else if (Available)
            {
                var Last_Time = DateTime.MinValue;
                if (Collection_Wait.ContainsKey(Name)) Last_Time = Collection_Wait[Name];
                var Now = DateTime.Now;
                if (Now.Subtract(Last_Time) >= TimeSpan.FromSeconds(10))
                {
                    Collection_Wait[Name] = Now;
                    try
                    {
                        var Collection_Information = Collection_Getter.Invoke(ID);
                        Individually_Set[Name] = Collection_Information.IndividualSet;
                        if (!Individually_Set[Name] && Collection_Information.EffectiveCollection.Id != Collection_Mapping[Name])
                        {
                            Log.Information($"Verified reassignment of {Name}: " + Collection_Assigner.Invoke(Collection_Mapping[Name], (int)ID_Mapping[Name]).ToString(), true);
                            Enable(Name);
                            return true;
                        }
                        else Log.Information($"{Name}'s collection is properly assigned.");
                    }
                    catch (Exception Error)
                    {
                        Log.Information($"Verified Reassignment: {Error.Message}");
                        Remove_Collection(Name);
                    }
                }
            }
            return false;
        }

        public static void Remove_Collection(string Name)
        {
            if (Collection_Mapping.ContainsKey(Name))
            {
                try
                {
                    Collection_Remover.Invoke(Collection_Mapping[Name]);
                }
                catch (Exception Error)
                {
                    Log.Error($"Remove Collection ({Name}): {Error.Message}");
                }
                Collection_Mapping.Remove(Name);
            }
        }

        public static void Set_Customize(string Name)
        {
            if (!Mods.ContainsKey(Name)) Load(Name);
            if (Mods.ContainsKey(Name)) if (Mods[Name].Bones != null) if (Mods[Name].Bones.Length > 0)
                    {
                        Customize.Set_Bones(ID_Mapping[Name], Mods[Name].Bones.ToString());
                        Log.Information("Customizing " + Name + "!");
                    }
        }
        private class Modification
        {
            public string Name;
            public string Description;
            public Dictionary<string, string> Files;
            public Dictionary<string, string> FileSwaps;
            public List<Object> Manipulations;
            public int AttributeMask = 0;
            [DefaultValue(0)]
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
            public int Priority = 0;
            public Dictionary<string, Tuple<string, bool>> Merge()
            {
                if (Files == null) Files = new();
                if (FileSwaps == null) FileSwaps = new();
                if (Manipulations == null) Manipulations = new List<Object>();
                if (Priority == null) Priority = 0;
                Dictionary<string, Tuple<string, bool>> Output = new Dictionary<string, Tuple<string, bool>>();
                foreach (var F in Files) Output.Add(F.Key, Tuple.Create(F.Value, true));
                foreach (var F in FileSwaps) Output.Add(F.Key, Tuple.Create(F.Value, false));
                return Output;
            }
        }
        private class Group
        {
            public string Name;
            public int Priority;
            public string Type;
            public uint DefaultSettings;
            public Object Identifier;
            public Object DefaultEntry;
            public List<Modification> Options;
            public bool AllVariants;
            public bool OnlyAttributes;
        }


        private class IMC_Entry
        {
            public int MaterialId = 0;
            public int DecalId = 0;
            public int VfxId = 0;
            public int MaterialAnimationId = 0;
            public int AttributeMask = 0;
            public int SoundId = 0;
        }
        private class IMC_Manipulation
        {
            public IMC_Entry Entry;
            public string ObjectType = "";
            public int PrimaryId = 0;
            public int SecondaryId = 0;
            public int Variant = 0;
            public string EquipSlot = "";
            public string BodySlot = "";
        }

        private class IMC
        {
            public string Type = "Imc";
            public IMC_Manipulation Manipulation;
        }

        private static Tuple<string, Dictionary<string, string>> Parse_Mod(string Name, Mod_Entry Settings, bool Check_Files = true)
        {
            var Output = new Dictionary<string, string>();
            try
            {
                var Manipulations = new List<Tuple<int, string>>();
                var Path = Rythmos_Path + $"\\Mods\\{Name}\\" + Settings.Item1;
                var Priority = Settings.Item2;
                var Default = JsonConvert.DeserializeObject<Modification>(File.ReadAllText(Path + "\\default_mod.json"));
                Default.Priority += Priority;
                var Mods = new List<Modification> { Default };
                foreach (var F in Directory.GetFiles(Path).ToList().FindAll(X => X.StartsWith($"{Path}\\group_")))
                {
                    var Data = JsonConvert.DeserializeObject<Group>(File.ReadAllText(F));
                    if (Data.Type == "Imc")
                    {
                        IMC I = new IMC();
                        I.Manipulation = new();
                        if (Data.Identifier != null) I.Manipulation = ((JObject)Data.Identifier).ToObject<IMC_Manipulation>();
                        I.Manipulation.Entry = new();
                        if (Data.DefaultEntry != null) I.Manipulation.Entry = ((JObject)Data.DefaultEntry).ToObject<IMC_Entry>();
                        if (Settings.Item3.ContainsKey(Data.Name)) foreach (var D in Data.Options) if (Settings.Item3[Data.Name].Contains(D.Name)) I.Manipulation.Entry.AttributeMask |= D.AttributeMask;
                        Manipulations.Add(Tuple.Create(Priority + Data.Priority, JsonConvert.SerializeObject(I)));
                        if (Data.AllVariants)
                        {
                            I.Manipulation.Variant = 1 - I.Manipulation.Variant;
                            Manipulations.Add(Tuple.Create(Priority + Data.Priority, JsonConvert.SerializeObject(I, Formatting.None)));
                        }
                    }
                    else if (Settings.Item3.ContainsKey(Data.Name)) foreach (var D in Data.Options) if (Settings.Item3[Data.Name].Contains(D.Name))
                            {
                                D.Priority += Priority + Data.Priority;
                                Mods.Add(D);
                                if (D.Manipulations != null) foreach (var M in D.Manipulations)
                                    {
                                        var Converted = JsonConvert.SerializeObject(M, Formatting.None);//Regex.Replace(JsonConvert.SerializeObject(M, Formatting.None), @"""(-?\d+(\.\d+)?([eE][+-]?\d+)?)""", "$1");
                                        Manipulations.Add(Tuple.Create(D.Priority, Converted));
                                    }
                            }
                }
                var Setter = new Dictionary<string, int>();
                foreach (var Mod in Mods) foreach (var Swap in Mod.Merge())
                    {
                        if (!Setter.ContainsKey(Swap.Key)) Setter.Add(Swap.Key, Mod.Priority);
                        if (Setter[Swap.Key] >= Mod.Priority)
                        {
                            if (!Output.ContainsKey(Swap.Key)) Output.Add(Swap.Key, Swap.Value.Item1);
                            if (Swap.Value.Item2 && (File.Exists(Path + "\\" + Swap.Value.Item1) || Types.All(X => !Swap.Value.Item1.StartsWith(X)))) Output[Swap.Key] = Path + "\\" + Swap.Value.Item1;
                            Setter[Swap.Key] = Mod.Priority;
                        }
                    }
                if (Default.Manipulations != null) foreach (var M in Default.Manipulations)
                    {
                        var Converted = JsonConvert.SerializeObject(M, Formatting.None);//Regex.Replace(JsonConvert.SerializeObject(M, Formatting.None), @"""(-?\d+(\.\d+)?([eE][+-]?\d+)?)""", "$1");
                        Manipulations.Add(Tuple.Create(Priority, Converted));
                    }

                var Final_Manipulations = new List<Object>();

                string O = null;

                var P = int.MinValue;

                var Found = new List<string>();

                foreach (var M in Manipulations)
                {
                    P = M.Item1;
                    O = M.Item2;
                    if (Found.Contains(O)) continue;
                    foreach (var N in Manipulations) if (O == N.Item2 && P < N.Item1)
                        {
                            P = N.Item1;
                            O = N.Item2;
                        }
                    Found.Add(O);
                    Final_Manipulations.Add(JsonConvert.DeserializeObject(O));
                }
                return Tuple.Create(Make(Final_Manipulations, 0), Output);
            }
            catch (Exception Error)
            {
                Log.Error("Parse Mod: " + Error.Message);
                return Tuple.Create("", Output);
            }
        }

        unsafe public static string Make(Object Data, byte Version)
        {
            try
            {
                var Raw = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Data, Formatting.None));
                using var S = new MemoryStream();
                using (var Zipped = new GZipStream(S, CompressionMode.Compress))
                {
                    Zipped.Write(new ReadOnlySpan<byte>(&Version, 1));
                    Zipped.Write(Raw, 0, Raw.Length);
                }
                var Output = Convert.ToBase64String(S.ToArray());
                return Output;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void Clean()
        {
            foreach (var Path in Directory.GetDirectories(Rythmos_Path + "\\Mods\\")) Load(Path.Split(Rythmos_Path + "\\Mods\\")[^1], true);
        }
        public static Mod_Configuration Gather_Mods(string Name)
        {
            // Later, I can provide a filtering argument of sorts, like a list of changed mods.
            var Settings = new Dictionary<string, Mod_Entry>(); // Mod -> (File Path, Priority, Group Settings)
            if (Objects.LocalPlayer != null)
            {
                foreach (var O in Objects) if (O.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc) if (Get_Name(O.ObjectIndex) == Name)
                        {
                            Log.Information($"Now gathering the mods of {O.Name.TextValue}.");
                            var Output = Collection_Getter.Invoke(O.ObjectIndex);
                            if (Output.ObjectValid)
                            {
                                var C = Collection_Getter.Invoke(O.ObjectIndex).EffectiveCollection.Id;
                                Log.Information($"The collection is {C}.");
                                var S = Settings_Getter.Invoke(C);
                                foreach (var Mod in S.Item2.Keys) if (S.Item2[Mod].Item1) Settings.Add(Mod, new Mod_Entry(Mod, S.Item2[Mod].Item2, S.Item2[Mod].Item3));
                                return new Mod_Configuration(Customize.Pack_Bones(O.ObjectIndex), Networking.C.Pack_Glamourer ? Glamour.Pack(O.ObjectIndex) : "", Get_Meta.Invoke(O.ObjectIndex), Settings, null, false);
                            }
                        }
                return new Mod_Configuration("", "", "", Settings, null, false);
            }
            return new Mod_Configuration("", "", "", Settings, null, false);
        }

        private static bool ZIP_Equality(string First, string Second)
        {
            const int Buffer = 1048576;
            using var First_Archive = ZipFile.OpenRead(First);
            using var Second_Archive = ZipFile.OpenRead(Second);
            var First_Entries = First_Archive.Entries.OrderBy(X => X.FullName).ToList();
            var Second_Entires = Second_Archive.Entries.OrderBy(X => X.FullName).ToList();
            if (First_Entries.Count != Second_Entires.Count) return false;
            for (var I = 0; I < First_Entries.Count; I++)
            {
                if (First_Entries[I].FullName != Second_Entires[I].FullName || First_Entries[I].Length != Second_Entires[I].Length) return false;
                using var First_Entry = First_Entries[I].Open();
                using var Second_Entry = Second_Entires[I].Open();
                var First_Buffer = new byte[Buffer];
                var Second_Buffer = new byte[Buffer];
                var First_Index = 0;
                var Second_Index = 0;
                while ((First_Index = First_Entry.Read(First_Buffer, 0, First_Buffer.Length)) > 0)
                {
                    Second_Index = Second_Entry.Read(Second_Buffer, 0, Second_Buffer.Length);
                    if (First_Index != Second_Index) return false;
                    for (var J = 0; J < First_Index; J++) if (First_Buffer[J] != Second_Buffer[J]) return false;
                }
            }
            return true;
        }
        public static Task<bool> Compile_Mods(string Name, Dictionary<string, HashSet<string>> Resources, string Customize_Data, string Glamourer_Data, string Meta, Dictionary<string, HashSet<string>>? VFX_Resources = null, bool Compress = false)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var Settings = new Dictionary<string, Mod_Entry>();
                    Settings["Mods"] = new Mod_Entry("Mods", 0, new Dictionary<string, List<string>>());
                    var Mod = new Modification();
                    Mod.Files = new();
                    Mod.FileSwaps = new();
                    Mod.Manipulations = new();
                    Mod.Priority = 0;
                    Mod.Description = "";
                    Mod.Name = "";
                    var D = Rythmos_Path + "\\Compressed\\" + Name;
                    if (Directory.Exists(D)) Directory.Delete(D, true);
                    Directory.CreateDirectory(D);
                    Directory.CreateDirectory(D + "\\Mods");
                    var Counter = new Dictionary<string, int>();
                    var Maximum = 0;
                    foreach (var Entry in Resources)
                    {
                        var Increased = false;
                        foreach (var File_Entry in Entry.Value.ToList())
                            if (Entry.Key != File_Entry) if (Entry.Key.Contains("\\"))
                                {
                                    var New = Entry.Key.Split("\\")[^1];
                                    if (!Counter.ContainsKey(New)) Counter.Add(New, 0);
                                    if (!Increased)
                                    {
                                        Counter[New]++;
                                        Increased = true;
                                    }
                                    if (Counter[New] > Maximum)
                                    {
                                        Maximum = Counter[New];
                                        Directory.CreateDirectory(D + $"\\Mods\\{Maximum}");
                                    }
                                    if (New.EndsWith(".tex") && Compress)
                                    {
                                        var S = new FileInfo(Entry.Key).Length;
                                        if (S > Threshold)
                                        {
                                            var Converted = D + $"\\Mods\\{Counter[New]}\\" + New.Replace(".tex", ".png");
                                            await Converter.Invoke(Entry.Key, Converted, Penumbra.Api.Enums.TextureType.Png, false);
                                            try
                                            {
                                                using var I = SixLabors.ImageSharp.Image.Load(Converted);
                                                Log.Information($"Compressing {New}...");
                                                I.Mutate(X =>
                                                {
                                                    if (I.Width >= I.Height)
                                                    {
                                                        X.Resize(1024, (int)Math.Round(((((double)I.Height) / ((double)I.Width)) * 1024)));
                                                    }
                                                    else X.Resize((int)Math.Round(((((double)I.Width) / ((double)I.Height)) * 1024)), 1024);
                                                });
                                                var Temporary = D + $"\\Mods\\{Counter[New]}\\Temporary.png";
                                                var Stream = new FileStream(Temporary, FileMode.Create);
                                                I.Save(Stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                                                I.Dispose();
                                                Stream.Dispose();
                                                await Converter.Invoke(Temporary, D + $"\\Mods\\{Counter[New]}\\" + New, Penumbra.Api.Enums.TextureType.AsIsTex, false);
                                                File.Delete(Converted);
                                                File.Delete(Temporary);
                                            }
                                            catch (Exception Error)
                                            {
                                                Log.Information(Error.Message);
                                            }
                                        }
                                        else File.Copy(Entry.Key, D + $"\\Mods\\{Counter[New]}\\" + New, true);
                                    }
                                    else File.Copy(Entry.Key, D + $"\\Mods\\{Counter[New]}\\" + New, true);
                                    Mod.Files.Add(File_Entry, $"{Counter[New]}\\" + New);
                                }
                                else Mod.FileSwaps.Add(File_Entry, Entry.Key);
                    }
                    if (VFX_Resources != null)
                    {
                        Counter = new();
                        Maximum = 0;
                        foreach (var Entry in VFX_Resources)
                        {
                            var Increased = false;
                            foreach (var File_Entry in Entry.Value.ToList()) if (Entry.Key != File_Entry) if (Entry.Key.Contains(":\\"))
                                    {
                                        Log.Information(Entry.Key);
                                        var New = Entry.Key.Split("\\")[^1];
                                        if (!Counter.ContainsKey(New)) Counter.Add(New, 0);
                                        if (!Increased)
                                        {
                                            Counter[New]++;
                                            Increased = true;
                                        }
                                        if (Counter[New] > Maximum)
                                        {
                                            Maximum = Counter[New];
                                            Directory.CreateDirectory(D + $"\\Mods\\V{Maximum}");
                                        }
                                        File.Copy(Entry.Key, D + $"\\Mods\\V{Counter[New]}\\" + New, true);
                                        Mod.Files.Add(File_Entry, $"V{Counter[New]}\\" + New);
                                    }
                                    else Mod.FileSwaps.Add(File_Entry, Entry.Key);
                        }
                    }
                    File.WriteAllBytes(D + "\\Mods\\default_mod.json", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Mod, Formatting.None)));
                    File.WriteAllBytes(D + "\\Configuration.json", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new Mod_Configuration(Customize_Data, Glamourer_Data, Meta, Settings, null, false), Formatting.None)));
                    ZipFile.CreateFromDirectory(D, D + " 1.zip");
                    if (File.Exists(D + ".zip"))
                    {
                        if (ZIP_Equality(D + ".zip", D + " 1.zip"))
                        {
                            File.Delete(D + " 1.zip");
                            return false;
                        }
                        File.Delete(D + ".zip");
                    }
                    File.Move(D + " 1.zip", D + ".zip");
                    Log.Information("A compressed pack has been created!");
                }
                catch (Exception Error)
                {
                    Log.Information(Error.Message);
                    return false;
                }
                return true;
            });
        }

        public static List<string> Traverse(ResourceNodeDto A)
        {
            List<string> Output = [A.ActualPath];
            foreach (var B in A.Children) Output.Add(B.ActualPath);
            return Output;
        }

        public static Task<bool> Pack(string Name, Mod_Configuration M, uint Type = 0, bool Compress = false)
        {
            var Index = ID_Mapping[Name];
            var Resources = Get_Resources.Invoke(Index)[0];
            return Task<bool>.Run(async () =>
            {
                Log.Information($"Packing ({Type}): " + Rythmos_Path + $"\\Compressed\\{Name}.zip");
                if (Type == 0)
                {
                    using (FileStream B = new FileStream(Rythmos_Path + $"\\Compressed\\{Name}.zip", FileMode.Create))
                    {
                        using (ZipArchive A = new(B, ZipArchiveMode.Create))
                        {
                            try
                            {
                                var Paths = new List<string>();
                                M.Mods.ToList().ForEach(X => Paths.Add(Penumbra_Path + "\\" + X.Value.Item1));
                                foreach (var File in Directory.EnumerateFiles(Penumbra_Path, "*", SearchOption.AllDirectories)) if (Paths.Any(X => File.StartsWith(X + "\\"))) A.CreateEntryFromFile(File, File.Substring(Penumbra_Path.Length + 1));
                                using (StreamWriter W = new StreamWriter(A.CreateEntry("Configuration.json").Open())) W.Write(JsonConvert.SerializeObject(M, Formatting.Indented));
                            }
                            catch (Exception Error)
                            {
                                Log.Information($"Pack: {Error.Message}");
                            }
                        }
                    }
                }
                else if (Type == 1)
                {
                    var VFX_Resources = new Dictionary<string, HashSet<string>>();

                    List<string> VFX_Textures = [];
                    Dictionary<string, List<string>> Textures = new();
                    foreach (var Penumbra_File in M.Mods.ToList())
                    {
                        var Path = Penumbra_Path + "\\" + Penumbra_File.Value.Item1;
                        Modification Default = null;
                        try
                        {
                            Default = JsonConvert.DeserializeObject<Modification>(File.ReadAllText(Path + "\\default_mod.json"));
                        }
                        catch (Exception Error)
                        {
                            Log.Error($"Packing {Penumbra_File.Key}: " + Error.Message);
                            continue;
                        }
                        var Mods = new List<Modification> { Default };
                        foreach (var F in Directory.GetFiles(Path).ToList().FindAll(X => X.StartsWith($"{Path}\\group_") && X.EndsWith(".json")))
                        {
                            Group Data = null;
                            try
                            {
                                Data = JsonConvert.DeserializeObject<Group>(File.ReadAllText(F));
                            }
                            catch (Exception Error)
                            {
                                Log.Error($"Packing {F} of {Penumbra_File.Key}: " + Error.Message);
                                continue;
                            }
                            if (Data.Type != "Imc") foreach (var D in Data.Options) if (!Penumbra_File.Value.Item3.ContainsKey(Data.Name))
                                    {
                                        Log.Error($"{D.Name} of {Penumbra_File.Key} was requested but not gathered as an option.");
                                        Mods.Add(D);
                                    }
                                    else if (Penumbra_File.Value.Item3[Data.Name].Contains(D.Name)) Mods.Add(D);
                        }
                        var Output = new Dictionary<string, string>();
                        var Setter = new Dictionary<string, int>();
                        foreach (var Mod in Mods) foreach (var Swap in Mod.Merge())
                            {
                                if (!Setter.ContainsKey(Swap.Key)) Setter.Add(Swap.Key, Mod.Priority);
                                if (Setter[Swap.Key] >= Mod.Priority)
                                {
                                    if (!Output.ContainsKey(Swap.Key)) Output.Add(Swap.Key, Swap.Value.Item1);
                                    if (Swap.Value.Item2 && (File.Exists(Path + "\\" + Swap.Value.Item1) || Types.All(X => !Swap.Value.Item1.StartsWith(X)))) Output[Swap.Key] = Path + "\\" + Swap.Value.Item1;
                                    Setter[Swap.Key] = Mod.Priority;
                                }
                            }
                        foreach (var O in Output) if (O.Value.EndsWith(".tex") || O.Value.EndsWith(".atex"))
                            {
                                if (!Textures.ContainsKey(O.Key.ToLower())) Textures.Add(O.Key.ToLower(), []);
                                Textures[O.Key.ToLower()].Add(O.Value.ToLower());
                            }
                            else if (O.Value.EndsWith(".avfx"))
                            {
                                if (File.Exists(O.Value))
                                {
                                    if (!VFX_Resources.ContainsKey(O.Value)) VFX_Resources.Add(O.Value, new());
                                    VFX_Resources[O.Value].Add(O.Key);
                                    var Split_Textures = string.Join("xeT", File.ReadAllText(O.Value).Split("xeT").Skip(1)).Split(".atex").SkipLast(1).Select(X => X + ".atex");
                                    foreach (var Texture in Split_Textures) for (var I = Texture.Length - 1; I >= 0; I--) if (((byte)Texture[I]) == 0)
                                            {
                                                var Texture_Name = Texture.Substring(I + 1).ToLower();
                                                if (!VFX_Textures.Contains(Texture_Name)) VFX_Textures.Add(Texture_Name);
                                                break;
                                            }
                                }
                                else Log.Error(O.Value + " does not exist.");
                            }
                            else if (O.Value.EndsWith(".pap") || O.Value.EndsWith(".tmb") || O.Value.EndsWith(".scd"))
                            {
                                if (!VFX_Resources.ContainsKey(O.Value)) VFX_Resources.Add(O.Value, new());
                                VFX_Resources[O.Value].Add(O.Key);
                            }
                    }
                    foreach (var Texture_Name in VFX_Textures) if (Textures.ContainsKey(Texture_Name)) foreach (var Required_File in Textures[Texture_Name])
                            {
                                if (!VFX_Resources.ContainsKey(Required_File)) VFX_Resources.Add(Required_File, new());
                                VFX_Resources[Required_File].Add(Texture_Name);
                            }
                    return await Compile_Mods(Name, Resources, M.Bones, M.Glamour, M.Meta, VFX_Resources, Compress);
                }
                else return await Compile_Mods(Name, Resources, M.Bones, M.Glamour, M.Meta, null, Compress);
                return true;
            });
        }
        public static bool Unpack(string Name)
        {
            var Zip_Path = Get_Newest(Name);
            if (Zip_Path != null)
            {
                if (Directory.Exists(Rythmos_Path + $"\\Mods\\{Name}")) Directory.Delete(Rythmos_Path + $"\\Mods\\{Name}", true);
                try
                {
                    ZipFile.ExtractToDirectory(Zip_Path, Rythmos_Path + $"\\Mods\\{Name}\\", true);
                    if (File.Exists(Rythmos_Path + $"\\Mods\\{Name}\\Configuration.json"))
                    {
                        File.SetLastWriteTime(Rythmos_Path + $"\\Mods\\{Name}\\Configuration.json", DateTime.Now);
                        return true;
                    }
                    else Log.Error($"Unpack: The configuration file for {Name} is missing.");
                }
                catch (Exception Error)
                {
                    File.Delete(Zip_Path);
                    Log.Error(Error.Message);
                    Log.Information($"Deleting the archive of {Name}.");
                    Log.Information($"Requesting the archive of {Name}.");
                    File_Time_Mapping[Name] = 0;
                }
            }
            return false;
        }

        public static bool Load(string Name, bool Check = false)
        {
            if (File.Exists(Rythmos_Path + $"\\Mods\\{Name}\\Configuration.json"))
            {
                Mods.Remove(Name);
                var Mod_Pack = JsonConvert.DeserializeObject<Mod_Configuration>(File.ReadAllText(Rythmos_Path + $"\\Mods\\{Name}\\Configuration.json"));
                if (Mod_Pack.External && !Server_Time_Mapping.ContainsKey(Name))
                {
                    Log.Information($"Removing the outdated pack of {Name}.");
                    try
                    {
                        Directory.Delete(Rythmos_Path + $"\\Mods\\{Name}", true);
                    }
                    catch (Exception Error)
                    {
                        Log.Error($"Loading: {Error.Message}");
                    }
                    return false;
                }
                if (!Check)
                {
                    Mods.Add(Name, Mod_Pack);
                    if (!Glamours.Keys.Contains(Name)) Glamours[Name] = Mods[Name].Glamour;
                }
                return true;
            }
            return false;
        }

        public static void Prepare(string Name)
        {
            try
            {
                if (Collection_Mapping.ContainsKey(Name) && Mods.ContainsKey(Name))
                {
                    if (!Path.Exists(Rythmos_Path + $"\\Mods\\{Name}\\Rythmos")) Directory.CreateDirectory(Rythmos_Path + $"\\Mods\\{Name}\\Rythmos");
                    Dictionary<string, (Tuple<string, Dictionary<string, string>>, int)> Mod_Data = new();
                    foreach (var Mod in Mods[Name].Mods) Mod_Data[Mod.Key] = (Parse_Mod(Name, Mod.Value), Mod.Value.Item2);
                    var Origins = new Dictionary<string, HashSet<string>>();
                    var Modifications = Mods[Name].Meta ?? "";
                    foreach (var Mod in Mod_Data) foreach (var Entry in Mod.Value.Item1.Item2)
                        {
                            if (!Origins.ContainsKey(Entry.Value)) Origins.Add(Entry.Value, new HashSet<string>());
                            Origins[Entry.Value].Add(Mod.Key);
                        }
                    foreach (var Mod in Mod_Data)
                    {
                        var Remove = new List<string>();
                        foreach (var Entry in Mod.Value.Item1.Item2)
                        {
                            if (Types.Any(Entry.Value.StartsWith))
                            {
                                if (!Data_Manager.FileExists(Entry.Value.Replace("\\", "/")) && Origins[Entry.Value].Count <= 1) Remove.Add(Entry.Key);
                            }
                            else if (!File.Exists(Entry.Value)) Remove.Add(Entry.Key);
                        }
                        foreach (var Key in Remove) Mod.Value.Item1.Item2.Remove(Key);
                        //foreach (var Key in Mod.Value.Item1.Item2)
                        //{
                        //    if (Key.Value.StartsWith(Rythmos_Path))
                        //    {
                        //        Mod.Value.Item1.Item2[Key.Key] = Rythmos_Path + $"\\Mods\\{Name}\\Rythmos\\" + Key.Value.Replace("/", "\\").Split("\\")[^1];
                        //        Log.Information(Key.Value);
                        //        Log.Information(Mod.Value.Item1.Item2[Key.Key]);
                        //        File.Copy(Key.Value, Mod.Value.Item1.Item2[Key.Key], true);
                        //    }
                        //}
                    }
                    if (Modifications.Length > 0) Temporary_Mod_Adder.Invoke(Name + " Manipulations", Collection_Mapping[Name], new Dictionary<string, string> { }, Modifications, 0);
                    foreach (var Mod in Mod_Data) Temporary_Mod_Adder.Invoke(Mod.Key, Collection_Mapping[Name], Mod.Value.Item1.Item2, Mod.Value.Item1.Item1, Mod.Value.Item2).ToString();
                }
            }
            catch (Exception Error)
            {
                Log.Error($"Prepare: {Error.Message}");
            }
        }

        public static void Enable(string Name)
        {
            if (Collection_Mapping.ContainsKey(Name) && Mods.ContainsKey(Name) && ID_Mapping.ContainsKey(Name))
            {
                try
                {
                    Set_Customize(Name);
                    Set_Glamour(Name, Glamours.ContainsKey(Name) ? Glamours[Name] ?? string.Empty : string.Empty);
                    Redraw_Character(Name);
                    if (Pets.ContainsKey(Name)) Redraw.Invoke(Pets[Name]);
                    if (Minions.ContainsKey(Name)) Redraw.Invoke(Minions[Name]);
                }
                catch (Exception Error)
                {
                    Log.Error("Enable: " + Error.Message);
                }
            }
        }

        public static void Update_Glamour(nint Address)
        {
            if (Objects.LocalPlayer != null ? Objects.LocalPlayer.Address == Address && Networking.C.Sync_Glamourer : false) Networking.Send(Encoding.UTF8.GetBytes(string.Join(", ", Entities) + "|" + Glamour.Pack(Objects.LocalPlayer.ObjectIndex)), 4);
        }

        public static void Set_Glamour(string Name, string Data)
        {
            if (Data != null && Name != Networking.Name)
            {
                Glamours[Name] = Data;
                if (ID_Mapping.ContainsKey(Name)) if (!(Data.Length > 2 ? Glamour.Set(ID_Mapping[Name], Data) : Glamour.Revert(ID_Mapping[Name]))) Recustomize.Add(Name);
            }
        }

        private static void Disable(string Name)
        {
            if (Collection_Mapping.ContainsKey(Name) && Mods.ContainsKey(Name))
            {
                foreach (var Mod in Mods[Name].Mods) Temporary_Mod_Remover.Invoke(Mod.Key, Collection_Mapping[Name], Mod.Value.Item2);
                Redraw_Character(Name);
            }
        }

        public static void Redraw_Character(string Name) => Redraw.Invoke((int)ID_Mapping[Name]);

        unsafe private static bool Update_Characters()
        {
            var Changed = false;
            try
            {
                var New_Friend = false;
                Entities.Clear();
                Pets.Clear();
                Minions.Clear();
                List<string> Previous_People = ID_Mapping.Keys.ToList<string>();
                foreach (var O in Objects.Shuffle()) if (O.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
                    {
                        var Name = Get_Name(O.ObjectIndex);
                        if (O.ObjectIndex == Objects.LocalPlayer?.ObjectIndex)
                        {
                            ID_Mapping[Name] = O.ObjectIndex;
                            continue;
                        }
                        if (!(((BattleChara*)O.Address)->IsFriend) && !Networking.C.Friends.Contains(Name)) continue;
                        if (Glamour.Ready) if (ID_Mapping.ContainsKey(Name)) if (ID_Mapping[Name] != O.ObjectIndex)
                                {
                                    Glamour.Unlock(ID_Mapping[Name]);
                                    Glamour.Revert(ID_Mapping[Name]);
                                    ID_Mapping.Remove(Name);
                                }
                        foreach (var I in Objects) if (I.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc || I.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion) if (I.OwnerId == O.GameObjectId)
                                {
                                    if (I.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)
                                    {
                                        Pets[Name] = I.ObjectIndex;
                                    }
                                    else Minions[Name] = I.ObjectIndex;
                                    break;
                                }
                        var Character_Changed = false;
                        if (!Glamour.Ready)
                        {
                            ID_Mapping[Name] = O.ObjectIndex;
                            Set_Customize(Name);
                            Character_Changed = true;
                        }
                        else Character_Changed = Set_Collection(O.ObjectIndex);
                        if (Recustomize.Contains(Name))
                        {
                            ID_Mapping[Name] = O.ObjectIndex;
                            Set_Customize(Name);
                            Log.Information($"Recustomizing {Name}.");
                            Recustomize.Remove(Name);
                            if (Glamour.Ready && Glamours.ContainsKey(Name)) Set_Glamour(Name, Glamours[Name]);
                        }
                        if (Mods.ContainsKey(Name) && Character_Changed) Changed = true;
                        Entities.Add(Name);
                        if (!Networking.C.Friends.Contains(Name))
                        {
                            Networking.C.Friends.Add(Name);
                            New_Friend = true;
                        }
                    }
                foreach (var Person in Previous_People) if (!Entities.Contains(Person)) Recustomize.Add(Person);
                if (New_Friend) Networking.C.Save();
            }
            catch (Exception Error)
            {
                Log.Error("Update Characters: " + Error.Message);
            }
            return Changed;
        }

        unsafe public static void Update(IFramework F)
        {
            try
            {
                if (Rythmos_Path.Length > 0 && Customize.Ready)
                {
                    if (!Client.IsLoggedIn && Objects.LocalPlayer is null)
                    {
                        foreach (var Key in Collection_Mapping.Keys.AsEnumerable<string>()) Remove_Collection(Key);
                        ID_Mapping = new();
                        T = 0;
                        Background_T = 0;
                        Request_T = 0;
                    }
                    else if (Objects.LocalPlayer is not null)
                    {
                        Available = !((BattleChara*)Objects.LocalPlayer.Address)->InCombat;
                        if (Glamour.Ready)
                        {
                            foreach (var O in Objects)
                            {
                                var Keys = ID_Mapping.Keys.ToList();
                                var Name = Get_Name(O.ObjectIndex);
                                foreach (var Key in Keys) if (ID_Mapping[Key] == O.ObjectIndex && Key != Name)
                                    {
                                        Log.Information($"The object index of {Key} has become that of {Name}.");
                                        Glamour.Unlock(O.ObjectIndex);
                                        Glamour.Revert(O.ObjectIndex);
                                        ID_Mapping.Remove(Key);
                                    }
                            }
                        }
                        var New_T = TimeProvider.System.GetTimestamp();
                        List<string> Party_Friends = [];
                        if (New_T - Background_T > 10000000 && Available)
                        {
                            var Proxy = InfoProxyCrossRealm.Instance();
                            var Party_Members = new List<string>();
                            if (Proxy->IsInCrossRealmParty)
                            {
                                Party_Members = Proxy->CrossRealmGroups[Proxy->LocalPlayerGroupIndex].GroupMembers.ToArray().Where(X => X.NameString.Length > 0).Select(X => X.NameString + " " + Data_Manager.GetExcelSheet<World>().GetRow((uint)X.HomeWorld).Name.ToString()).ToList();
                            }
                            else foreach (var Member in Party) Party_Members.Add(Member.Name + " " + Member.World.Value.Name.ExtractText());
                            foreach (var Friend in Party_Members) if (Networking.C.Friends.Contains(Friend))
                                {
                                    Party_Friends.Add(Friend);
                                    if (!Collection_Mapping.ContainsKey(Friend)) if (Create_Collection(Friend))
                                        {
                                            Background_T = New_T;
                                            break;
                                        }
                                }
                        }
                        if (Available) Update_Characters();
                        if (Glamour.Ready)
                        {
                            foreach (var Setting in Glamour_Buffer) if (ID_Mapping.ContainsKey(Setting.Key)) Set_Glamour(Setting.Key, Setting.Value);
                            Glamour_Buffer = new Dictionary<string, string>(Glamour_Buffer.Where(X => !ID_Mapping.ContainsKey(X.Key)));
                        }
                        var Everyone = Networking.C.Friends.FindAll(X => Entities.Contains(X) || Party_Friends.Contains(X));
                        if (Available && !Networking.Downloading && New_T - Request_T > 100000000) foreach (var Friend in Everyone) if (Friend != Networking.Name) if (File_Time_Mapping.ContainsKey(Friend) && Server_Time_Mapping.ContainsKey(Friend) ? File_Time_Mapping[Friend] < Server_Time_Mapping[Friend] : Server_Time_Mapping.ContainsKey(Friend) && (Locked.ContainsKey(Friend) ? !Locked[Friend] : true))
                                    {
                                        Log.Information($"Requesting {Friend}!");
                                        Request_T = New_T;
                                        Networking.Send(Encoding.UTF8.GetBytes(Friend), 2);
                                        break;
                                    }
                    }
                    else
                    {
                        foreach (var Person in Collection_Mapping.Keys) if (!Recustomize.Contains(Person)) Recustomize.Add(Person);
                        foreach (var Person in ID_Mapping.Keys) if (!Recustomize.Contains(Person)) Recustomize.Add(Person);
                    }
                }
            }
            catch (Exception Error) { Log.Error("Character Update: " + Error.Message); }
        }

        public static void Dispose()
        {
            if (Redraw_Handler != null) Redraw_Handler.Dispose();
            foreach (var Name in Collection_Mapping.Keys.ToList()) Remove_Collection(Name);
        }
    }
}
