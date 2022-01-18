using Discord;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Command
{
    public sealed class ReactionEventWrapper : IDisposable
    {
        public IUserMessage Message { get; }
        public event Action<SocketReaction> OnReactionAdded = delegate { };
        public event Action<SocketReaction> OnReactionRemoved = delegate { };
        public event Action OnReactionsCleared = delegate { };

        public ReactionEventWrapper(DiscordSocketClient client, IUserMessage msg)
        {
            Message = msg ?? throw new ArgumentNullException(nameof(msg));
            _client = client;

            _client.ReactionAdded += Discord_ReactionAdded;
            _client.ReactionRemoved += Discord_ReactionRemoved;
            _client.ReactionsCleared += Discord_ReactionsCleared;
        }

        private Task Discord_ReactionsCleared(Cacheable<IUserMessage, ulong> user, Cacheable<IMessageChannel, ulong> msg)
        {
            Task.Run(() =>
            {
                try
                {
                    if (msg.Id == Message.Id)
                        OnReactionsCleared?.Invoke();
                }
                catch { }
            });

            return Task.CompletedTask;
        }

        private Task Discord_ReactionRemoved(Cacheable<IUserMessage, ulong> user, Cacheable<IMessageChannel, ulong> msg, SocketReaction reaction)
        {
            Task.Run(() =>
            {
                try
                {
                    if (msg.Id == Message.Id)
                        OnReactionRemoved?.Invoke(reaction);
                }
                catch { }
            });

            return Task.CompletedTask;
        }

        private Task Discord_ReactionAdded(Cacheable<IUserMessage, ulong> arg1, Cacheable<IMessageChannel, ulong> msg, SocketReaction reaction)
        {
            Task.Run(() =>
            {
                try
                {
                    if (msg.Id == Message.Id)
                        OnReactionAdded?.Invoke(reaction);
                }
                catch { }
            });

            return Task.CompletedTask;
        }

        public void UnsubAll()
        {
            _client.ReactionAdded -= Discord_ReactionAdded;
            _client.ReactionRemoved -= Discord_ReactionRemoved;
            _client.ReactionsCleared -= Discord_ReactionsCleared;
            OnReactionAdded = null;
            OnReactionRemoved = null;
            OnReactionsCleared = null;
        }

        private bool disposing = false;
        private readonly DiscordSocketClient _client;

        public void Dispose()
        {
            if (disposing)
                return;
            disposing = true;
            UnsubAll();
        }
    }
}
