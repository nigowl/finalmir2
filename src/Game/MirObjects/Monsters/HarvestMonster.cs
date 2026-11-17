using Server.MirDatabase;
using S = ServerPackets;

namespace Server.MirObjects.Monsters
{
    public class HarvestMonster : MonsterObject
    {
        protected short Quality;
        protected int RemainingSkinCount;

        private List<UserItem> _drops;


        protected internal HarvestMonster(MonsterInfo info)
            : base(info)
        {
            RemainingSkinCount = 2;
        }

        public static bool GroundSupply(DropInfo drop) => !drop.QuestRequired && drop.GroupedDrop == null &&
            (drop.Gold > 0 || drop.Item?.Type == ItemType.Potion && drop.Item.Shape is 0 or 1 && (drop.Item.Stats[Stat.HP] > 0 || drop.Item.Stats[Stat.MP] > 0));
        protected override void Drop() { Drop(Info.Drops.Where(GroundSupply)); }
        public override bool Harvest(PlayerObject player)
        {
            if (RemainingSkinCount == 0)
            {
                for (int i = _drops.Count - 1; i >= 0; i--)
                {
                    if (player.CheckGroupQuestItem(_drops[i]))
                    {
                        _drops.RemoveAt(i); 
                    }
                    else
                    {
                        if (player.CanGainItem(_drops[i]))
                        {
                            player.GainItem(_drops[i]);
                            _drops.RemoveAt(i);
                        }
                    }
                }

                if (_drops.Count == 0)
                {
                    Harvested = true;
                    _drops = null;
                    Broadcast(new S.ObjectHarvested { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });
                }
                else player.ReceiveChat(GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.YouCannotCarryAnymore), ChatType.System);

                return true;
            }

            if (--RemainingSkinCount > 0) return true;


            _drops = new List<UserItem>();

            for (int i = 0; i < Info.Drops.Count; i++)
            {
                DropInfo drop = Info.Drops[i];
                if (GroundSupply(drop)) continue;

                var reward = drop.AttemptDrop(EXPOwner?.Stats[Stat.ItemDropRatePercent] ?? 0, EXPOwner?.Stats[Stat.GoldDropRatePercent] ?? 0);

                if (reward != null)
                {
                    foreach (var dropItem in reward.Items)
                    {
                        UserItem item = Envir.CreateDropItem(dropItem);
                        if (item == null) continue;

                        if (drop.QuestRequired)
                        {
                            if (!player.CheckGroupQuestItem(item, false)) continue;
                        }

                        if (item.Info.Type == ItemType.Meat)
                        {
                            item.CurrentDura = (ushort)Math.Max(0, item.CurrentDura + Quality);
                        }

                        _drops.Add(item);
                    }
                }
            }

            if (_drops.Count == 0)
            {
                player.ReceiveChat(GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.NothingWasFound), ChatType.System);
                Harvested = true;
                _drops = null;
                Broadcast(new S.ObjectHarvested { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });
            }

            return true;
        }

        /*
        public override bool MagAttacked(MonsterObject A, int Damage)
        {
            bool B = base.MagAttacked(A, Damage);

            if (B)
                Quality = (short)Math.Max(short.MinValue, Quality - 2000);
            return true;
        }*/
    }
}
