using System.Text.RegularExpressions;
using Server.MirEnvir;

namespace Server.MirDatabase
{
    public class MonsterInfo
    {
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }

        protected static Envir EditEnvir
        {
            get { return Envir.Edit; }
        }

        protected static MessageQueue MessageQueue
        {
            get { return MessageQueue.Instance; }
        }

        public int Index;
        public string Name = string.Empty;

        public Monster Image;
        public byte AI, Effect, ViewRange = 7, CoolEye;
        public ushort Level;

        public byte Light;

        public ushort AttackSpeed = 2500, MoveSpeed = 1800;
        public uint Experience;

        public string DropPath = "";
        
        public List<DropInfo> Drops = new List<DropInfo>();

        public bool CanTame = true, CanPush = true, AutoRev = true, Undead = false;
        public bool CanRecall = false;
        public bool IsBoss = false;

        public bool HasSpawnScript;
        public bool HasDieScript;

        public Stats Stats;

        public MonsterInfo()
        {
            Stats = new Stats();
        }

        public MonsterInfo(BinaryReader reader)
        {
            Index = reader.ReadInt32();
            Name = reader.ReadString();

            Image = (Monster) reader.ReadUInt16();
            AI = reader.ReadByte();
            Effect = reader.ReadByte();

            if (Envir.LoadVersion < 62)
            {
                Level = (ushort)reader.ReadByte();
            }
            else
            {
                Level = reader.ReadUInt16();
            }

            ViewRange = reader.ReadByte();
            CoolEye = reader.ReadByte();

            if (Envir.LoadVersion > 84)
            {
                Stats = new Stats(reader);
            }

            if (Envir.LoadVersion <= 84)
            {
                Stats = new Stats();
                Stats[Stat.HP] = (int)reader.ReadUInt32(); //Monster form prevented greater than ushort, so this should never overflow.
            }

            if (Envir.LoadVersion < 62)
            {
                Stats[Stat.MinAC] = reader.ReadByte();
                Stats[Stat.MaxAC] = reader.ReadByte();
                Stats[Stat.MinMAC] = reader.ReadByte();
                Stats[Stat.MaxMAC] = reader.ReadByte();
                Stats[Stat.MinDC] = reader.ReadByte();
                Stats[Stat.MaxDC] = reader.ReadByte();
                Stats[Stat.MinMC] = reader.ReadByte();
                Stats[Stat.MaxMC] = reader.ReadByte();
                Stats[Stat.MinSC] = reader.ReadByte();
                Stats[Stat.MaxSC] = reader.ReadByte();
            }
            else
            {
                if (Envir.LoadVersion <= 84)
                {
                    Stats[Stat.MinAC] = reader.ReadUInt16();
                    Stats[Stat.MaxAC] = reader.ReadUInt16();
                    Stats[Stat.MinMAC] = reader.ReadUInt16();
                    Stats[Stat.MaxMAC] = reader.ReadUInt16();
                    Stats[Stat.MinDC] = reader.ReadUInt16();
                    Stats[Stat.MaxDC] = reader.ReadUInt16();
                    Stats[Stat.MinMC] = reader.ReadUInt16();
                    Stats[Stat.MaxMC] = reader.ReadUInt16();
                    Stats[Stat.MinSC] = reader.ReadUInt16();
                    Stats[Stat.MaxSC] = reader.ReadUInt16();
                }
            }

            if (Envir.LoadVersion <= 84)
            {
                Stats[Stat.Accuracy] = reader.ReadByte();
                Stats[Stat.Agility] = reader.ReadByte();
            }

            Light = reader.ReadByte();

            AttackSpeed = reader.ReadUInt16();
            MoveSpeed = reader.ReadUInt16();

            Experience = reader.ReadUInt32();

            CanPush = reader.ReadBoolean();
            CanTame = reader.ReadBoolean();

            if (Envir.LoadVersion < 18) return;
            AutoRev = reader.ReadBoolean();
            Undead = reader.ReadBoolean();

            if (Envir.LoadVersion < 89) return;

            DropPath = reader.ReadString();

            if (Envir.LoadVersion >= 115)
            {
                CanRecall = reader.ReadBoolean();
            }

            if (Envir.LoadVersion >= 116)
            {
                IsBoss = reader.ReadBoolean();
            }
        }

        public string GameName
        {
            get { return Regex.Replace(Name, @"[\d-]", string.Empty); }
        }

        public ClientMonsterInfo ClientInformation
        {
            get
            {
                Stats tooltipStats = new Stats();
                tooltipStats[Stat.HP] = Stats[Stat.HP];
                tooltipStats[Stat.MinAC] = Stats[Stat.MinAC];
                tooltipStats[Stat.MaxAC] = Stats[Stat.MaxAC];
                tooltipStats[Stat.MinMAC] = Stats[Stat.MinMAC];
                tooltipStats[Stat.MaxMAC] = Stats[Stat.MaxMAC];
                tooltipStats[Stat.MinDC] = Stats[Stat.MinDC];
                tooltipStats[Stat.MaxDC] = Stats[Stat.MaxDC];
                tooltipStats[Stat.MinMC] = Stats[Stat.MinMC];
                tooltipStats[Stat.MaxMC] = Stats[Stat.MaxMC];
                tooltipStats[Stat.MinSC] = Stats[Stat.MinSC];
                tooltipStats[Stat.MaxSC] = Stats[Stat.MaxSC];

                return new ClientMonsterInfo
                {
                    Index = Index,
                    Name = Name,
                    GameName = GameName,
                    Image = Image,
                    AI = AI,
                    Effect = Effect,
                    ViewRange = ViewRange,
                    CoolEye = CoolEye,
                    Level = Level,
                    Light = Light,
                    AttackSpeed = AttackSpeed,
                    MoveSpeed = MoveSpeed,
                    Experience = Experience,
                    CanTame = CanTame,
                    CanPush = CanPush,
                    AutoRev = AutoRev,
                    Undead = Undead,
                    Stats = tooltipStats
                };
            }
        }

        public void Save(BinaryWriter writer)
        {
            writer.Write(Index);
            writer.Write(Name);

            writer.Write((ushort) Image);
            writer.Write(AI);
            writer.Write(Effect);
            writer.Write(Level);
            writer.Write(ViewRange);
            writer.Write(CoolEye);

            Stats.Save(writer);


            writer.Write(Light);

            writer.Write(AttackSpeed);
            writer.Write(MoveSpeed);

            writer.Write(Experience);

            writer.Write(CanPush);
            writer.Write(CanTame);
            writer.Write(AutoRev);
            writer.Write(Undead);

            writer.Write(DropPath);
            writer.Write(CanRecall);
            writer.Write(IsBoss);
        }

        public static void FromText(string text)
        {
            string[] data = text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (data.Length < 28) return; //28

            MonsterInfo info = new MonsterInfo {Name = data[0]};
            ushort image;
            if (!ushort.TryParse(data[1], out image)) return;
            info.Image = (Monster) image;

            if (!byte.TryParse(data[2], out info.AI)) return;
            if (!byte.TryParse(data[3], out info.Effect)) return;
            if (!ushort.TryParse(data[4], out info.Level)) return;
            if (!byte.TryParse(data[5], out info.ViewRange)) return;

            //if (!int.TryParse(data[6], out info.HP)) return;

            //if (!ushort.TryParse(data[7], out info.MinAC)) return;
            //if (!ushort.TryParse(data[8], out info.MaxAC)) return;
            //if (!ushort.TryParse(data[9], out info.MinMAC)) return;
            //if (!ushort.TryParse(data[10], out info.MaxMAC)) return;
            //if (!ushort.TryParse(data[11], out info.MinDC)) return;
            //if (!ushort.TryParse(data[12], out info.MaxDC)) return;
            //if (!ushort.TryParse(data[13], out info.MinMC)) return;
            //if (!ushort.TryParse(data[14], out info.MaxMC)) return;
            //if (!ushort.TryParse(data[15], out info.MinSC)) return;
            //if (!ushort.TryParse(data[16], out info.MaxSC)) return;
            //if (!byte.TryParse(data[17], out info.Accuracy)) return;
            //if (!byte.TryParse(data[18], out info.Agility)) return;
            if (!byte.TryParse(data[19], out info.Light)) return;

            if (!ushort.TryParse(data[20], out info.AttackSpeed)) return;
            if (!ushort.TryParse(data[21], out info.MoveSpeed)) return;

            if (!uint.TryParse(data[22], out info.Experience)) return;
            
            if (!bool.TryParse(data[23], out info.CanTame)) return;
            if (!bool.TryParse(data[24], out info.CanPush)) return;

            if (!bool.TryParse(data[25], out info.AutoRev)) return;
            if (!bool.TryParse(data[26], out info.Undead)) return;
            if (!byte.TryParse(data[27], out info.CoolEye)) return;

            //int count;

            //if (!int.TryParse(data[27], out count)) return;

            //if (28 + count * 3 > data.Length) return;

            info.Index = ++EditEnvir.MonsterIndex;
            EditEnvir.MonsterInfoList.Add(info);
        }
        public string ToText()
        {
            return "";// string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27}", Name, (ushort)Image, AI, Effect, Level, ViewRange,
              //  HP, 
                //MinAC, MaxAC, MinMAC, MaxMAC, MinDC, MaxDC, MinMC, MaxMC, MinSC, MaxSC, 
               // Accuracy, Agility, Light, AttackSpeed, MoveSpeed, Experience, CanTame, CanPush, AutoRev, Undead, CoolEye);
        }

        public override string ToString()
        {
            return string.Format("{0}: {1}", Index, Name);
            //return string.Format("{0}", Name);
        }
    }

    public class DropRewardInfo
    {
        public List<ItemInfo> Items;
        public uint Gold;
    }

    public class GroupDropInfo : List<DropInfo>
    {
        public bool Random;
        public bool First;
    }

    public class DropInfo
    {
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }

        protected static MessageQueue MessageQueue
        {
            get { return MessageQueue.Instance; }
        }

        public int Chance;
        public decimal? Probability;
        public decimal BaseProbability => Probability ?? (Chance > 0 ? 1m / Chance : 0m);
        public ItemInfo Item;
        public uint Gold;
        public GroupDropInfo GroupedDrop;

        public byte Type;
        public bool QuestRequired;
        public int Count = 1;

        public static DropInfo FromLine(string s)
        {
            string[] parts = s.Split(new[] {' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                return null;
            }

            DropInfo info = new DropInfo();

            if (parts[0].EndsWith("%"))
            {
                if (!decimal.TryParse(parts[0][..^1], System.Globalization.NumberStyles.AllowDecimalPoint,
                    System.Globalization.CultureInfo.InvariantCulture, out var percent) || percent <= 0 || percent > 100) return null;
                info.Probability = percent / 100m;
                if (info.Probability == 0) return null;
            }
            else if (!parts[0].StartsWith("1/") || !int.TryParse(parts[0].Substring(2), out info.Chance) || info.Chance < 1) return null;

            if (string.Compare(parts[1], "Gold", StringComparison.OrdinalIgnoreCase) == 0)
            {
                if (parts.Length < 3) return null;
                if (!uint.TryParse(parts[2], out info.Gold) || info.Gold == 0) return null;
            }
            else if (parts[1].ToUpperInvariant() is "GROUP" or "GROUP*" or "GROUP^")
            {
                info.GroupedDrop = new GroupDropInfo
                {
                    Random = parts[1].EndsWith("*"),
                    First = parts[1].EndsWith("^")
                };
            }
            else
            {
                info.Item = Envir.GetItemInfo(parts[1]);
                if (info.Item == null) return null;

                info.QuestRequired = parts.Length > 2 && parts[2].Equals("Q", StringComparison.OrdinalIgnoreCase);
                var quantity = false;
                for (var index = 2; index < parts.Length; index++)
                {
                    if (parts[index].Equals("Q", StringComparison.OrdinalIgnoreCase) || parts[index].StartsWith("LV", StringComparison.OrdinalIgnoreCase)) continue;
                    var value = parts[index].StartsWith("COUNT=", StringComparison.OrdinalIgnoreCase) ? parts[index].Substring(6) : parts[index];
                    if (quantity || !int.TryParse(value, out var count) || count is < 1 or > 100) return null;
                    info.Count = count; quantity = true;
                }
                if (quantity) info.QuestRequired = parts.Skip(2).Any(part => part.Equals("Q", StringComparison.OrdinalIgnoreCase));
            }

            return info;
        }

        public static void Load(List<DropInfo> list, string name, string path, byte type = 0, bool createIfNotExists = true)
        {
            if (!File.Exists(path))
            {
                if (createIfNotExists)
                {
                    string[] contents = new[]
                        {
                        ";Pots + Other", string.Empty, string.Empty,
                        ";Weapons", string.Empty, string.Empty,
                        ";Armour", string.Empty, string.Empty,
                        ";Helmets", string.Empty, string.Empty,
                        ";Necklace", string.Empty, string.Empty,
                        ";Bracelets", string.Empty, string.Empty,
                        ";Rings", string.Empty, string.Empty,
                        ";Shoes", string.Empty, string.Empty,
                        ";Belts", string.Empty, string.Empty,
                        ";Stone",
                    };

                    File.WriteAllLines(path, contents);
                }

                return;
            }

            list.AddRange(Parse(File.ReadAllText(path), name, type, sort: false));
            Sort(list);
        }

        public static List<DropInfo> Parse(string content, string name, byte type = 0, Func<string, string> read = null, bool sort = true)
        {
            var list = new List<DropInfo>();
            var lines = ParseInsert(content.Replace("\r\n", "\n").Split('\n').ToList(), read);

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith(";") || string.IsNullOrWhiteSpace(lines[i])) continue;

                DropInfo drop = DropInfo.FromLine(lines[i]);

                if (drop == null)
                {
                    MessageQueue.Enqueue(GameLanguage.ServerTextMap.GetLocalization((ServerTextKeys.CouldNotLoadDropLine), name, lines[i]));
                    continue;
                }

                if (drop.GroupedDrop != null)
                {
                    ParseGroup(drop, name, lines, i + 1);
                }

                drop.Type = type;

                list.Add(drop);
            }

            if (DropComposition.IsComposed(content)) list = DropComposition.Merge(list);
            if (sort) Sort(list);
            return list;
        }

        private static void Sort(List<DropInfo> list)
        {
            list.Sort((drop1, drop2) =>
            {
                if (drop1.Gold > 0 && drop2.Gold == 0)
                    return 1;
                if (drop1.Gold == 0 && drop2.Gold > 0)
                    return -1;

                if (drop1.Item == null || drop2.Item == null) return 0;

                return drop1.Item.Type.CompareTo(drop2.Item.Type);
            });
        }

        private static void ParseGroup(DropInfo parentDrop, string name, List<string> lines, int startLine)
        {
            bool start = false, finish = false;

            var drops = new List<DropInfo>();

            for (int j = startLine; j < lines.Count; j++)
            {
                var line = lines[j].Trim();
                lines[j] = "";

                if (line.Trim() == ("{"))
                {
                    start = true;
                    continue;
                }

                if (line.Trim() == ("}"))
                {
                    finish = true;
                    break;
                }

                if (line.StartsWith(";") || string.IsNullOrWhiteSpace(line)) continue;

                var drop = DropInfo.FromLine(line);

                if (drop == null)
                {
                    MessageQueue.Enqueue(GameLanguage.ServerTextMap.GetLocalization((ServerTextKeys.CouldNotLoadDropLine), name, line));
                    continue;
                }

                if (drop.GroupedDrop != null)
                {
                    ParseGroup(drop, name, lines, j + 1);
                }

                drops.Add(drop);
            }

            if (start && finish)
            {
                parentDrop.GroupedDrop.AddRange(drops);
            }
        }

        private static List<string> ParseInsert(List<string> lines, Func<string, string> read)
        {
            var expanded = lines.Select(line => (Line: line, Chain: new HashSet<string>(StringComparer.Ordinal))).ToList();
            var root = Path.GetFullPath(Settings.DropPath).TrimEnd(Path.DirectorySeparatorChar);
            for (var i = 0; i < expanded.Count; i++)
            {
                if (expanded.Count > 10000) throw new InvalidDataException("掉落展开超过10000行。");
                var (line, chain) = expanded[i];
                var match = Regex.Match(line.Trim(), @"^#INSERT\s+\[(.+)\]$", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                var path = Mir2.Hosting.ProjectPaths.SafePath(root, match.Groups[1].Value.Replace('\\', '/'));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || chain.Count >= 16 || chain.Contains(path))
                    throw new InvalidDataException("掉落引用越界、循环或超过16层。");
                var next = new HashSet<string>(chain, StringComparer.Ordinal) { path };
                var children = read == null ? File.ReadAllLines(path) : read(Path.GetRelativePath(root, path).Replace('\\', '/')).Replace("\r\n", "\n").Split('\n');
                expanded.AddRange(children.Select(child => (child, next)));
            }
            return expanded.Select(entry => entry.Line).Where(line => !line.TrimStart().StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public decimal AdjustedProbability(float multiplier, int offset = 0)
        {
            if (multiplier <= 0 || BaseProbability <= 0) return 0;
            return offset >= 100 ? 1 : Math.Min(1m, BaseProbability * (decimal)multiplier / (1m - Math.Max(0, offset) / 100m));
        }

        public DropRewardInfo AttemptDrop(int itemDropRatePercentOffset = 0, int goldDropRatePercentOffset = 0)
        {
            if (Probability.HasValue)
            {
                if (Envir.Random.NextDouble() >= (double)AdjustedProbability(Settings.DropRate, itemDropRatePercentOffset)) return null;
            }
            else
            {
                int rate = (int)(Chance / Settings.DropRate);
                if (itemDropRatePercentOffset > 0) rate -= (rate * itemDropRatePercentOffset) / 100;
                if (Envir.Random.Next(Math.Max(1, rate)) != 0) return null;
            }

            uint gold = 0;
            var items = new List<ItemInfo>();

            if (Gold > 0)
            {
                int lowerGoldRange = (int)(Gold / 2);
                int upperGoldRange = (int)(Gold + Gold / 2);

                if (goldDropRatePercentOffset > 0)
                {
                    lowerGoldRange += (lowerGoldRange * goldDropRatePercentOffset) / 100;
                }

                if (lowerGoldRange > upperGoldRange) lowerGoldRange = upperGoldRange;

                gold = (uint)Envir.Random.Next(lowerGoldRange, upperGoldRange);
            }
            else if (Item != null)
            {
                // 数量属于一次掉落判定，不能拆成多次独立概率而改变实际爆率。
                for (var i = 0; i < Count; i++) items.Add(Item);
            }
            else if (GroupedDrop != null)
            {
                var tempItems = new List<ItemInfo>();

                foreach (var item in GroupedDrop)
                {
                    var reward = item.AttemptDrop(itemDropRatePercentOffset, goldDropRatePercentOffset);

                    if (reward != null)
                    {
                        gold += reward.Gold;

                        tempItems.AddRange(reward.Items);

                        if (GroupedDrop.First)
                        {
                            break;
                        }
                    }
                }

                if (GroupedDrop.Random)
                {
                    if (tempItems.Count > 0)
                    {
                        items.Add(tempItems[Envir.Random.Next(tempItems.Count)]);
                    }
                }
                else
                {
                    items.AddRange(tempItems);
                }
            }

            return new DropRewardInfo
            {
                Gold = gold,
                Items = items
            };
        }
    }
}
