using System;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Stl.Concurrency;
using Stl.Fusion.Bridge.Messages;
using Stl.Fusion.Internal;
using Stl.Reflection;
using Stl.Time;

namespace Stl.Fusion.Bridge.Internal
{
    public interface ISubscriptionProcessorFactory
    {
        public SubscriptionProcessor Create(Type genericType,
            IPublication publication, Channel<Message> outgoingMessages,
            TimeSpan subscribeTimeout, IMomentClock clock, ILoggerFactory loggerFactory);
    }

    public sealed class SubscriptionProcessorFactory : ISubscriptionProcessorFactory
    {
        private delegate SubscriptionProcessor Constructor(
            IPublication publication, Channel<Message> outgoingMessages,
            TimeSpan subscribeTimeout, IMomentClock clock, ILoggerFactory loggerFactory);

        private static readonly ConcurrentDictionary<Type, Constructor> ConstructorCache =
            new ConcurrentDictionary<Type, Constructor>();
        private static readonly Func<Type, Constructor> CreateCache = Create;

        public static SubscriptionProcessorFactory Instance { get; } = new SubscriptionProcessorFactory();

        private SubscriptionProcessorFactory() { }

        public SubscriptionProcessor Create(Type genericType,
            IPublication publication, Channel<Message> outgoingMessages,
            TimeSpan subscribeTimeout, IMomentClock clock, ILoggerFactory loggerFactory)
            => ConstructorCache
                .GetOrAddChecked(genericType, CreateCache)
                .Invoke(publication, outgoingMessages, subscribeTimeout, clock, loggerFactory);

        private static Constructor Create(Type genericType)
        {
            if (!genericType.IsGenericTypeDefinition)
                throw Errors.TypeMustBeOpenGenericType(genericType);

            var handler = new FactoryApplyHandler(genericType);

            SubscriptionProcessor Factory(
                IPublication publication, Channel<Message> outgoingMessages,
                TimeSpan subscribeTimeout, IMomentClock clock, ILoggerFactory loggerFactory)
                => publication.Apply(handler, (outgoingMessages, subscribeTimeout, clock, loggerFactory));

            return Factory;
        }

        private class FactoryApplyHandler : IPublicationApplyHandler<
            (Channel<Message> OutgoingMessages, TimeSpan SubscribeTimeout, IMomentClock Clock, ILoggerFactory loggerFactory),
            SubscriptionProcessor>
        {
            private readonly Type _genericType;
            private readonly ConcurrentDictionary<Type, Type> _closedTypeCache =
                new ConcurrentDictionary<Type, Type>();

            public FactoryApplyHandler(Type genericType)
                => _genericType = genericType;

            public SubscriptionProcessor Apply<T>(
                IPublication<T> publication,
                (Channel<Message> OutgoingMessages, TimeSpan SubscribeTimeout, IMomentClock Clock, ILoggerFactory loggerFactory) arg)
            {
                var closedType = _closedTypeCache.GetOrAddChecked(
                    typeof(T),
                    (tArg, tGeneric) => tGeneric.MakeGenericType(tArg),
                    _genericType);
                return (SubscriptionProcessor) closedType.CreateInstance(
                    publication, arg.OutgoingMessages, arg.SubscribeTimeout, arg.Clock, arg.loggerFactory);
            }
        }
    }
}
