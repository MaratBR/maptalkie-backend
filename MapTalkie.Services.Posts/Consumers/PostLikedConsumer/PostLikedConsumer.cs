using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapTalkie.DB.Context;
using MapTalkie.Domain.Messages.Posts;
using MassTransit;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MapTalkie.Services.Posts.Consumers.PostLikedConsumer
{
    public class PostLikedConsumer : IConsumer<Batch<PostEngagement>>
    {
        private static DateTime? RefreshScheduledAt = null;
        private static SemaphoreSlim RefreshSemaphore = new SemaphoreSlim(1, 1);
        private readonly IMemoryCache _cache;
        private readonly AppDbContext _context;
        private readonly ILogger<PostLikedConsumer> _logger;

        private long MaxUpdates = 100; // пока что пусть будет так
        private long UpdatesCount = 0;

        public PostLikedConsumer(AppDbContext context, IMemoryCache cache, ILogger<PostLikedConsumer> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<Batch<PostEngagement>> context)
        {
            await PublishEngagement(context);
            Interlocked.Add(ref UpdatesCount, context.Message.Length);

            if (Interlocked.Read(ref UpdatesCount) > MaxUpdates)
            {
                Interlocked.And(ref UpdatesCount, 0);
                // тут может произойти race-condition, но я просто это проигнорирую,
                // потому что это функция вызывается редко
                await context.Send(new PostRankDecayRefresher.RefreshRankDecay(.0002, .25));
            }

            _logger.LogDebug("Consumed {0} with {1} messages inside", nameof(Batch<PostEngagement>),
                context.Message.Length);
        }

        private async Task PublishEngagement(ConsumeContext<Batch<PostEngagement>> context)
        {
            var postIds = context.Message.Select(c => c.Message.PostId).Distinct().ToList();

            foreach (var dbEngagement in _context.Posts
                .Where(p => postIds.Contains(p.Id) && p.Available)
                .Select(p => new { p.Id, p.Location, p.CachedCommentsCount, p.CachedLikesCount, p.CachedSharesCount }))
            {
                await context.Publish(new EngagementUpdate
                {
                    PostId = dbEngagement.Id,
                    Likes = dbEngagement.CachedLikesCount,
                    Shares = dbEngagement.CachedSharesCount,
                    Comments = dbEngagement.CachedCommentsCount,
                    Location = dbEngagement.Location
                });
            }
        }
    }
}