// lets players change the color of their name via chat commands
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using ConVar;
using UnityEngine;
using CompanionServer;
using Pool = Facepunch.Pool;
using Random = System.Random;

namespace Oxide.Plugins
{
    [Info("Username color choice", "slightlypug", "0.0.1")]
    [Description("Colors user names using ROK registry system.")]

    public class ColoredNames : CovalencePlugin
    {
        #region Fields
        private readonly StringBuilder _sharedStringBuilder = new StringBuilder();
        private const string ColorRegex = "^#(?:[0-9a-fA-F]{3}){1,2}$";
        private const string ChatFormat = "{0}: {1}";
        private readonly Random _random = new Random();
        private string GetRndColor() => $"#{_random.Next(0x1000000):X6}";
        #endregion

        #region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>()
            {
                { "NoPermission", "You can't use this command." },
                { "NoPermissionGradient", "You can't use {0} gradients." },
                { "NoPermissionRandom", "You can't use random {0} color." },
                { "NoPermissionRainbow", "You can't use the rainbow color." },
                { "IncorrectGradientUsage", "Incorrect usage! To use gradients please use /{0} gradient hexCode1 hexCode2 ...</color>" },
                { "IncorrectGradientUsageArgs", "Incorrect usage! A gradient requires at least two different valid colors!"},
                { "GradientChanged", "{0} gradient changed to {1}!"},
                { "IncorrectUsage", "Incorrect usage! /{0} <color>\nFor detailed help do /{1}" },
                { "IncorrectSetUsage", "Incorrect set usage! /{0} set <playerIdOrName> <colorOrColorArgument>\nFor a list of colors do /colors" },
                { "PlayerNotFound", "Player {0} was not found." },
                { "ColorRemoved", "{0} color removed!" },
                { "ColorChanged", "{0} color changed to <color={1}>{1}</color>!" },
                { "ColorsInfo", "You can only use hexcodes, eg '<color=#ffff94>#ffff94</color>'\nTo remove your color, use 'clear', 'reset' or 'remove'\n\nAvailable Commands: {0}\n\n{1}"},
                { "RndColor", "{0} color was randomized to <color={1}>{1}</color>" },
                { "RainbowColor", "{0} color was set to rainbow." },
                { "IncorrectGroupUsage", "Incorrect group usage! /{0} group <groupName> <colorOrColorArgument>\nFor a list of colors do /colors" },
            }, this);
        }
        #endregion

        #region Config
        private Configuration _configuration;
        private class Configuration
        {
            // General
            [JsonProperty(PropertyName = "Rainbow colors")]
            public string[] RainbowColors = { "#ff0000", "#ff00b3ff", "#f700ffff", "#5500ffff", "#00fff7ff", "#00ff5eff", "#fffb00ff" };

            // Name
            [JsonProperty(PropertyName = "Name color commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string NameColorCommands = "color";
            [JsonProperty(PropertyName = "Name color commands (help)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string NameColorsCommandH = "colorH";
            [JsonProperty(PropertyName = "Name show color permission")]
            public string NamePermShow = "colorednames.name.show";
            [JsonProperty(PropertyName = "Name use permission")]
            public string NamePermUse = "colorednames.name.use";
            [JsonProperty(PropertyName = "Name use gradient permission")]
            public string NamePermGradient = "colorednames.name.gradient";
            [JsonProperty(PropertyName = "Name default rainbow name permission")]
            public string NamePermRainbow = "colorednames.name.rainbow";
            [JsonProperty(PropertyName = "Name bypass restrictions permission")]
            public string NamePermBypass = "colorednames.name.bypass";
            [JsonProperty(PropertyName = "Name get random color permission")]
            public string NamePermRandomColor = "colorednames.name.random";
            [JsonProperty(PropertyName = "Inactivity removal time (days)")]
            public int InactivityRemovalTime = 0;
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            _configuration = new Configuration();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _configuration = Config.ReadObject<Configuration>();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_configuration);

        #region Color Range Class
        public class ColorRange
        {
            [JsonProperty(PropertyName = "From")]
            public string _from;
            [JsonProperty(PropertyName = "To")]
            public string _to;

            public ColorRange(string from, string to)
            {
                _from = from;
                _to = to;
            }
        }
        #endregion
        #endregion

        #region Delta
        private Dictionary<string, CachePlayerData> cachedData = new Dictionary<string, CachePlayerData>();
        private StoredData storedData;
        private Dictionary<string, PlayerData> allColorData => storedData.AllColorData;
        private class StoredData
        {
            [JsonProperty("AllColorData")]
            public Dictionary<string, PlayerData> AllColorData { get; private set; } = new Dictionary<string, PlayerData>();
        }
        private class PlayerData
        {
            [JsonProperty("Name Color")]
            public string NameColor = string.Empty;
            [JsonProperty("Name Gradient Args")]
            public string[] NameGradientArgs = null;
            [JsonProperty("Last active")]
            public long LastActive = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            public PlayerData()
            {

            }
            public PlayerData(string nameColor = "", string[] nameGradientArgs = null, string messageColor = "", string[] messageGradientArgs = null)
            {
                NameColor = nameColor;
                NameGradientArgs = nameGradientArgs;
            }
            public PlayerData(bool isGroup)
            {
                LastActive = 0;
            }
        }
        private class CachePlayerData
        {
            public string NameColorGradient;
            public string PrimaryGroup;
            public CachePlayerData(string nameColorGradient = "", string primaryGroup = "")
            {
                NameColorGradient = nameColorGradient;
                PrimaryGroup = primaryGroup;
            }
        }
        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);
        private void OnServerSave()
        {
            ClearUpData();
            SaveData();
        }
        private void Unload() => SaveData();
        private void ChangeNameColor(string key, string color, string[] colorArgs)
        {
            var playerData = new PlayerData(color, colorArgs);
            if (!allColorData.ContainsKey(key)) allColorData.Add(key, playerData);

            allColorData[key].NameColor = color;
            allColorData[key].NameGradientArgs = colorArgs;
        }
        #endregion

        #region Hooks
        private void Init()
        {
            permission.RegisterPermission(_configuration.NamePermShow, this);
            permission.RegisterPermission(_configuration.NamePermRainbow, this);
            permission.RegisterPermission(_configuration.NamePermGradient, this);
            permission.RegisterPermission(_configuration.NamePermUse, this);
            permission.RegisterPermission(_configuration.NamePermBypass, this);
            permission.RegisterPermission(_configuration.NamePermRandomColor, this);

            AddCovalenceCommand(_configuration.NameColorCommands, nameof(cmdNameColor));

            storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);

            ClearUpData();
        }
        private void OnUserConnected(IPlayer player)
        {
            if (!allColorData.ContainsKey(player.Id)) return;
            allColorData[player.Id].LastActive = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        private void OnUserDisconnected(IPlayer player)
        {
            if (!allColorData.ContainsKey(player.Id)) return;
            allColorData[player.Id].LastActive = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        private void OnUserNameUpdated(string id, string oldName, string newName) => ClearCache(id);
        private void OnUserGroupAdded(string id, string groupName) => ClearCache(id);
        private void OnUserGroupRemoved(string id, string groupName) => ClearCache(id);
        private void OnGroupDeleted(string name) => ClearCache();
        private void OnGroupPermissionGranted(string name, string perm) => ClearCache();
        private void OnGroupPermissionRevoked(string name, string perm) => ClearCache();
        object OnUserChat(IPlayer player, string message)
        {
            var colored = FromMessage(player, ConVar.Chat.ChatChannel.Global, message);
            server.Broadcast(colored.GetChatOutput());  // uses your ChatFormat
            return true; // cancel default chat
        }
        #region Commands
        void cmdNameColor(IPlayer player, string cmd, string[] args) => ProcessColorCommand(player, cmd, args);
        #endregion

        #region Helpers
        private void ClearUpData()
        {
            if (_configuration.InactivityRemovalTime == 0) return;

            var copy = new Dictionary<string, PlayerData>(allColorData);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var kv in copy)
            {
                var pd = kv.Value;
                if (pd.LastActive == 0) continue;
                if (pd.LastActive + (_configuration.InactivityRemovalTime * 86400L) < now)
                    allColorData.Remove(kv.Key);
            }
        }
        private void ClearCache()
        {
            cachedData.Clear();
        }
        private void ClearCache(string Id)
        {
            cachedData.Remove(Id);
        }
        private string GetCorrectLang(bool isGroup, bool isMessage, string key) =>
            isGroup
                ? (isMessage ? $"Group {key} message" : $"Group {key} name")
                : (isMessage ? "Message" : "Name");
        private bool IsValidColor(string input) => Regex.Match(input, ColorRegex).Success;

        // Name
        private bool HasNameShowPerm(IPlayer player) => true;
        private bool HasNamePerm(IPlayer player) => true;
        private bool HasNameRainbow(IPlayer player) => true;
        private bool CanNameGradient(IPlayer player) => true;
        private bool CanNameBypass(IPlayer player) => true;
        private bool CanNameRandomColor(IPlayer player) => true;
        private string GetMessage(string key, IPlayer player = null, params object[] args)
        {
            var msg = lang.GetMessage(key, this, player?.Id);
            return (args == null || args.Length == 0) ? msg : string.Format(msg, args);
        }
        private void ProcessColorCommand(IPlayer player, string cmd, string[] args, bool isMessage = false)
        {
            if (args == null || args.Length < 1) return;

            string colLower;

            if (args[0].Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 3) { player.Reply(GetMessage("IncorrectSetUsage", player, cmd)); return; }

                var target = covalence.Players.FindPlayer(args[1]);
                if (target == null)
                {
                    player.Reply(GetMessage("PlayerNotFound", player, args[1]));
                    return;
                }

                colLower = args[2].ToLowerInvariant();
                ProcessColor(player, target, colLower, args.Skip(3).ToArray(), isMessage);
            }
            else if (args[0].Equals("group", StringComparison.OrdinalIgnoreCase))
            {
                if (!player.IsAdmin) { player.Reply(GetMessage("NoPermission", player)); return; }
                if (args.Length < 3) { player.Reply(GetMessage("IncorrectGroupUsage", player, cmd)); return; }

                var group = args[1];
                if (!permission.GroupExists(group))
                    permission.CreateGroup(group, string.Empty, 0);

                colLower = args[2].ToLowerInvariant();
                ProcessColor(player, player, colLower, args.Skip(3).ToArray(), isMessage, group);
            }
            else
            {
                if (!isMessage && !HasNamePerm(player)) return;
                colLower = args[0].ToLowerInvariant();
                ProcessColor(player, player, colLower, args.Skip(1).ToArray(), isMessage);
            }
        }

        private void ProcessColor(IPlayer player, IPlayer target, string colLower, string[] colors, bool isMessage = false, string groupName = "")
        {
            var isGroup = !string.IsNullOrEmpty(groupName);
            var isCalledOnto = player != target && !isGroup;
            var key = isGroup ? groupName : target.Id;

            if (!isGroup && !allColorData.ContainsKey(target.Id)) allColorData.Add(target.Id, new PlayerData());
            else if (isGroup && !allColorData.ContainsKey(groupName)) allColorData.Add(groupName, new PlayerData(true));

            if (colLower == "gradient")
            {
                if ((!isMessage && !CanNameGradient(player)))
                {
                    player.Reply(GetMessage("NoPermissionGradient", player, isMessage ? "message" : "name"));
                    return;
                }

                if (colors.Length < 2) return;

                string gradientName = ProcessGradient(isMessage ? "Example Message" : target.Name, colors, isMessage, player);
                if (gradientName.Equals(string.Empty)) return;

                if (isMessage)
                {
                    // Do nothing
                }
                else
                {
                    allColorData[key].NameColor = string.Empty;
                    allColorData[key].NameGradientArgs = colors;
                    if (!isGroup)
                    {
                        if (!cachedData.ContainsKey(key)) cachedData.Add(key, new CachePlayerData(gradientName, GetPrimaryUserGroup(player.Id)));
                        else cachedData[key].NameColorGradient = gradientName;
                    }
                }
                if (isGroup) ClearCache();
                if (target.IsConnected) target.Reply(GetMessage("GradientChanged", target, GetCorrectLang(isGroup, isMessage, key), gradientName));
                return;
            }
            if (colLower == "reset" || colLower == "clear" || colLower == "remove")
            {
                if (isMessage)
                {
                    // Do nothing
                }
                else
                {
                    allColorData[key].NameColor = string.Empty;
                    allColorData[key].NameGradientArgs = null;
                    if (cachedData.ContainsKey(key)) cachedData.Remove(key);
                }

                if (string.IsNullOrEmpty(allColorData[key].NameColor) &&
                    allColorData[key].NameGradientArgs == null)
                {
                    allColorData.Remove(key);
                }

                if (isGroup)
                {
                    ClearCache();
                }

                if (target.IsConnected) target.Reply(GetMessage("ColorRemoved", target, GetCorrectLang(isGroup, isMessage, key)));
                return;
            }
            if (colLower == "random")
            {
                if (!isMessage && !CanNameRandomColor(player))
                {
                    player.Reply(GetMessage("NoPermissionRandom", player, isMessage ? "message" : "name"));
                    return;
                }
                colLower = GetRndColor();
                if (isMessage)
                {
                    // Do nothing
                }
                else
                {
                    ChangeNameColor(key, colLower, null);
                }
                if (isGroup) ClearCache();

                if (target.IsConnected) target.Reply(GetMessage("RndColor", target, GetCorrectLang(isGroup, isMessage, key), colLower));
                return;
            }
            if (colLower == "rainbow")
            {
                if (!HasNameRainbow(player))
                {
                    player.Reply(GetMessage("NoPermissionRainbow", player));
                    return;
                }
                if (isMessage)
                {
                    // Do nothing
                }
                else
                {
                    ChangeNameColor(key, string.Empty, _configuration.RainbowColors);
                }

                if (isGroup) ClearCache();
                if (target.IsConnected) target.Reply(GetMessage("RainbowColor", target, GetCorrectLang(isGroup, isMessage, key)));
                return;
            }

            if (isMessage)
            {
                // Do nothing
            }
            else
            {
                ChangeNameColor(key, colLower, null);
            }

            if (target.IsConnected) target.Reply(GetMessage("ColorChanged", target, isMessage ? "Message" : "Name", colLower));
            if (isGroup) ClearCache();
        }
        private string ProcessGradient(string name, string[] colorArgs, bool isMessage = false, IPlayer iPlayer = null)
        {
            var gradientName = _sharedStringBuilder;
            gradientName.Clear();

            var colors = Pool.GetList<Color>();
            Color startColor, endColor;

            int nameLength = name.Length;
            int segmentCount = colorArgs.Length - 1;
            if (segmentCount <= 0) return string.Empty;

            int stepsPerSegment = Math.Max(1, nameLength / segmentCount);

            if (stepsPerSegment <= 1)
            {
                for (int i = 0; i < nameLength; i++)
                {
                    var idx = Math.Min(i, colorArgs.Length - 1);
                    ColorUtility.TryParseHtmlString(colorArgs[idx], out startColor);
                    colors.Add(startColor);
                }
            }
            else
            {
                int gradientIterations = Math.Min(segmentCount, nameLength);
                for (int i = 0; i < gradientIterations; i++)
                {
                    if (colors.Count >= nameLength) break;

                    ColorUtility.TryParseHtmlString(colorArgs[i], out startColor);
                    if (i >= colorArgs.Length - 1) endColor = startColor;
                    else ColorUtility.TryParseHtmlString(colorArgs[i + 1], out endColor);

                    GetAndAddGradients(startColor, endColor, stepsPerSegment, colors);

                    if (colors.Count > nameLength)
                        colors.RemoveRange(nameLength, colors.Count - nameLength);
                }

                while (colors.Count < nameLength)
                {
                    ColorUtility.TryParseHtmlString(colorArgs[colorArgs.Length - 1], out endColor);
                    colors.Add(endColor);
                }
            }

            for (int i = 0; i < nameLength; i++)
            {
                gradientName.Append("<color=#")
                    .Append(ColorUtility.ToHtmlStringRGB(colors[i]))
                    .Append(">")
                    .Append(name[i])
                    .Append("</color>");
            }

            Pool.FreeList(ref colors);
            return gradientName.ToString();
        }

        /// <summary>
        /// Gets and adds gradient colors to provided results list
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="steps"></param>
        /// <param name="results"></param>
        private void GetAndAddGradients(Color start, Color end, int steps, List<Color> results)
        {
            if (steps <= 1) { results.Add(end); return; }

            float stepR = (end.r - start.r) / (steps - 1f);
            float stepG = (end.g - start.g) / (steps - 1f);
            float stepB = (end.b - start.b) / (steps - 1f);

            for (int i = 0; i < steps; i++)
                results.Add(new Color(start.r + stepR * i, start.g + stepG * i, start.b + stepB * i));
        }
        private string GetPrimaryUserGroup(string Id)
        {
            var groups = permission.GetUserGroups(Id);

            var primaryGroup = string.Empty;
            var groupRank = -1;
            foreach (var group in groups)
            {
                if (!allColorData.ContainsKey(group)) continue;
                var currentGroupRank = permission.GetGroupRank(group);
                if (currentGroupRank > groupRank)
                {
                    groupRank = currentGroupRank;
                    primaryGroup = group;
                }
            }
            return primaryGroup;
        }
        private ColoredChatMessage FromMessage(IPlayer player, Chat.ChatChannel channel, string message)
        {
            if (!allColorData.TryGetValue(player.Id, out var playerData) || playerData == null)
                playerData = new PlayerData();

            var coloredNameData = GetColoredName(player, playerData);
            coloredNameData.ChatChannel = channel;
            coloredNameData.Message = message; // <-- missing before
            return coloredNameData;
        }
        private ColoredChatMessage GetColoredName(IPlayer player, PlayerData playerData)
        {
            string playerUserName = player.Name;
            string playerColor = player.IsAdmin ? "#af5" : "#5af";
            string playerColorNonModified = playerColor;

            if (!cachedData.TryGetValue(player.Id, out var cachedPlayerData) || cachedPlayerData == null)
            {
                var gradientName = string.Empty;
                if (playerData?.NameGradientArgs != null)
                    gradientName = ProcessGradient(player.Name, playerData.NameGradientArgs, false, player);

                cachedData[player.Id] = cachedPlayerData = new CachePlayerData(gradientName, GetPrimaryUserGroup(player.Id));
            }

            if (HasNameShowPerm(player))
            {
                // Player-level overrides
                if (playerData?.NameGradientArgs != null)
                {
                    playerUserName = cachedPlayerData.NameColorGradient;
                }
                else if (!string.IsNullOrEmpty(playerData?.NameColor))
                {
                    playerColor = playerData.NameColor;
                }
                else if (playerUserName == player.Name && !string.IsNullOrEmpty(cachedPlayerData.NameColorGradient))
                {
                    playerUserName = cachedPlayerData.NameColorGradient;
                }
            }

            // Group-level overrides (if no player-specific override applied)
            if (allColorData.ContainsKey(cachedPlayerData.PrimaryGroup))
            {
                var groupData = allColorData[cachedPlayerData.PrimaryGroup];
                if (playerUserName == player.Name && playerColor == playerColorNonModified)
                {
                    if (groupData?.NameGradientArgs != null)
                    {
                        playerUserName = string.IsNullOrEmpty(cachedPlayerData.NameColorGradient)
                            ? (cachedPlayerData.NameColorGradient = ProcessGradient(player.Name, groupData.NameGradientArgs, false, player))
                            : cachedPlayerData.NameColorGradient;
                    }
                    else if (!string.IsNullOrEmpty(groupData?.NameColor))
                    {
                        playerColor = groupData.NameColor;
                    }
                }
            }

            return new ColoredChatMessage
            {
                Player = player,
                Name = playerUserName,
                Color = (playerColor == playerColorNonModified) ? string.Empty : playerColor
            };
        }
        private bool IsInHexRange(string hexCode, string rangeHexCode1, string rangeHexCode2)
        {
            Color mainColor;
            ColorUtility.TryParseHtmlString(hexCode, out mainColor);
            Color start;
            ColorUtility.TryParseHtmlString(rangeHexCode1, out start);
            Color end;
            ColorUtility.TryParseHtmlString(rangeHexCode2, out end);

            if ((mainColor.r >= start.r && mainColor.r <= end.r) &&
                (mainColor.g >= start.g && mainColor.g <= end.g) &&
                (mainColor.b >= start.b && mainColor.b <= end.b))
            {
                return true;
            }

            return false;
        }
        #endregion

        #region API
        private string API_GetNameColorHex(IPlayer player)
        {
            var playerData = new PlayerData();
            if (allColorData.ContainsKey(player.Id))
                playerData = allColorData[player.Id];

            var coloredData = GetColoredName(player, playerData);
            if (string.IsNullOrEmpty(coloredData.Color))
            {
                return player.IsAdmin ? "#af5" : "#5af";
            }

            return coloredData.Color;
        }
        private string API_GetColoredName(IPlayer player)
        {
            var playerData = allColorData.ContainsKey(player.Id) ? allColorData[player.Id] : new PlayerData();
            var coloredData = GetColoredName(player, playerData);
            if (!string.IsNullOrEmpty(coloredData.Color))
                return $"<color={coloredData.Color}>{player.Name}</color>";
            return coloredData.Name;
        }
        private string API_GetColoredChatMessage(IPlayer iPlayer, Chat.ChatChannel channel, string message)
        {
            var coloredChatMessage = FromMessage(iPlayer, channel, message);
            var formattedMessage = coloredChatMessage.GetChatOutput();
            return formattedMessage;
        }
        public struct ColoredChatMessage
        {
            public IPlayer Player;
            public Chat.ChatChannel ChatChannel;
            public string Name;
            public string Color;
            public string Message;
            private static readonly Dictionary<string, object> _coloredChatDictionary = new Dictionary<string, object>();
            public ColoredChatMessage(IPlayer player, Chat.ChatChannel chatChannel, string name, string color, string message)
            {
                Player = player;
                ChatChannel = chatChannel;
                Name = name;
                Color = color;
                Message = message;
            }

            /// <summary>
            /// Gets colored chat dictionary
            /// <remarks>Note: this is a shared dictionary instance over all ColoredChatMessage's, do not store random stuff in here!</remarks>
            /// </summary>
            /// <returns></returns>
            public Dictionary<string, object> GetDictionary()
            {
                _coloredChatDictionary[nameof(Player)] = Player;
                _coloredChatDictionary[nameof(ChatChannel)] = ChatChannel;
                _coloredChatDictionary[nameof(Name)] = Name;
                _coloredChatDictionary[nameof(Color)] = Color;
                _coloredChatDictionary[nameof(Message)] = Message;
                return _coloredChatDictionary;

            }
            public static ColoredChatMessage FromDictionary(Dictionary<string, object> dict)
            {
                return new ColoredChatMessage()
                {
                    Player = dict[nameof(Player)] as IPlayer,
                    ChatChannel = (Chat.ChatChannel)dict[nameof(ChatChannel)],
                    Name = dict[nameof(Name)] as string,
                    Color = dict[nameof(Color)] as string,
                    Message = dict[nameof(Message)] as string,
                };
            }
            public string GetChatOutput()
            {
                return string.Format(ChatFormat, $"</color={Color}>{Name}</color>", Message);
            }
        }
        #endregion

        #endregion
    }
}
