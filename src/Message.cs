namespace RollCallBot
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;

    public class Message
    {
        public IUserMessage userMessage { get; private set; }
        private IEmbed embed;

        // Public so MessageHandler can access it
        public struct VotingOption
        {
            public string emote;
            public string label;
            public List<IUser> users;

            public VotingOption(string emote, string label)
            {
                this.emote = emote;
                this.label = label;
                this.users = new List<IUser>();
            }

            public override string ToString()
            {
                return $"{emote} = {label} ({users.Count})";
            }
        }

        // Public so handler can iterate
        public List<VotingOption> VotingOptions = new();

        private string description;
        private SocketGuild guild;

        // Constructor for NEW roll call messages
        public Message(string description)
        {
            this.description = description ?? "Roll Call started!";

            VotingOptions.Add(new VotingOption("✅", "In"));
            VotingOptions.Add(new VotingOption("❌", "Out"));
            VotingOptions.Add(new VotingOption("❓", "Maybe"));
        }

        // Constructor for EXISTING messages (rebuild from embed)
        public Message(IUserMessage userMessage)
        {
            this.userMessage = userMessage;

            // Ensure guild is always set
            guild = (userMessage.Channel as SocketGuildChannel)?.Guild;

            embed = userMessage.Embeds.First();
            description = embed.Description;

            var pattern = new Regex(@"(.*) = (.*) \((\d+)\)");

            foreach (var embedField in embed.Fields)
            {
                var match = pattern.Match(embedField.Name);
                if (match.Success)
                {
                    var votingOption = new VotingOption(match.Groups[1].Value, match.Groups[2].Value);
                    VotingOptions.Add(votingOption);
                }
            }

            // Rebuild user lists from reactions
            var task = Task.Run(() => react(userMessage));
            task.Wait();
        }

        private async Task react(IUserMessage userMessage)
        {
            foreach (var reaction in userMessage.Reactions)
            {
                var users = userMessage.GetReactionUsersAsync(reaction.Key, 100);

                await foreach (var batch in users)
                {
                    foreach (var user in batch)
                    {
                        Add(user, reaction.Key.Name);
                    }
                }
            }
        }

        // Mutually exclusive: remove user from all options
        private void RemoveUserFromAllOptions(IUser user)
        {
            foreach (var option in VotingOptions)
                option.users.RemoveAll(u => u.Id == user.Id);
        }

        public void Add(IUser user, string emote)
        {
            if (user.IsBot)
                return;

            // ⭐ FIX: ensure guild is ALWAYS set, even before Send()
            guild ??=
                (userMessage?.Channel as SocketGuildChannel)?.Guild ??
                (user as SocketGuildUser)?.Guild;

            // Mutually exclusive: remove from all first
            RemoveUserFromAllOptions(user);

            // Add to selected option
            var users = FindCorrectUserList(emote);
            users.Add(user);
        }

        private List<IUser> FindCorrectUserList(string emote)
        {
            if (emote.StartsWith("au"))
            {
                return emote.EndsWith("dead")
                    ? VotingOptions.First(x => x.label == "Out").users
                    : VotingOptions.First(x => x.label == "In").users;
            }

            return VotingOptions.First(x => x.emote == emote).users;
        }

        public void Remove(IUser user, string emote)
        {
            var users = FindCorrectUserList(emote);
            users?.RemoveAll(x => x.Id == user.Id);
        }

        private string GetNickname(IUser user)
        {
            var u = guild?.GetUser(user.Id);
            return u?.Nickname ?? u?.Username ?? user.Username;
        }

        private EmbedFieldBuilder b(VotingOption votingOption)
        {
            var list = string.Join('\n',
                votingOption.users.Select((d, i) => $"{i + 1}. {GetNickname(d)}"));

            if (string.IsNullOrEmpty(list))
                list = "1. ";

            return new EmbedFieldBuilder()
                .WithName($"{votingOption.emote} = {votingOption.label} ({votingOption.users.Count})")
                .WithValue(list)
                .WithIsInline(true);
        }

        public Embed RebuildEmbed()
        {
            var fields = VotingOptions.Select(b);
            var unix = userMessage?.CreatedAt.ToUnixTimeSeconds() ?? DateTimeOffset.Now.ToUnixTimeSeconds();

            return new EmbedBuilder()
                .WithTitle($"Roll Call – <t:{unix}:f>")
                .WithDescription(description)
                .WithFields(fields)
                .WithFooter("MikuRollCalling! v1")
                .Build();
        }

        public async Task Send(ISocketMessageChannel socketMessageChannel)
        {
            guild = (socketMessageChannel as SocketGuildChannel)?.Guild;

            embed = RebuildEmbed();
            var msg = await socketMessageChannel.SendMessageAsync(null, false, embed as Embed);

            var emojis = VotingOptions.Select(x => new Emoji(x.emote) as IEmote);
            await msg.AddReactionsAsync(emojis.ToArray());

            userMessage = msg;
        }

        public async Task UpdateAsync(IUserMessage message)
        {
            guild = (message.Channel as SocketGuildChannel)?.Guild;

            embed = RebuildEmbed();
            await message.ModifyAsync(x => x.Embed = embed as Embed);
        }
    }
}
