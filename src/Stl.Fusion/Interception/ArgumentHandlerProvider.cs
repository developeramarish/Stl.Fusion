using System;
using System.Reflection;
using Stl.DependencyInjection;
using Stl.Extensibility;

namespace Stl.Fusion.Interception
{
    public interface IArgumentHandlerProvider
    {
        ArgumentHandler GetInvocationTargetHandler(MethodInfo methodInfo, Type invocationTargetType);
        ArgumentHandler GetArgumentHandler(MethodInfo methodInfo, ParameterInfo parameterInfo);
    }

    public class ArgumentHandlerProvider : IArgumentHandlerProvider, IHasServiceProvider
    {
        public class Options : IOptions
        {
            public IMatchingTypeFinder MatchingTypeFinder { get; set; } =
                new MatchingTypeFinder(Assembly.GetExecutingAssembly());
        }

        protected IMatchingTypeFinder MatchingTypeFinder { get; }
        public IServiceProvider ServiceProvider { get; }

        public ArgumentHandlerProvider(Options? options, IServiceProvider serviceProvider)
        {
            options = options.OrDefault(serviceProvider);
            MatchingTypeFinder = options.MatchingTypeFinder;
            ServiceProvider = serviceProvider;
        }

        public ArgumentHandler GetInvocationTargetHandler(MethodInfo methodInfo, Type invocationTargetType)
            => GetArgumentComparer(invocationTargetType, true);

        public virtual ArgumentHandler GetArgumentHandler(MethodInfo methodInfo, ParameterInfo parameterInfo)
            => GetArgumentComparer(parameterInfo.ParameterType);

        public virtual ArgumentHandler GetArgumentComparer(Type type, bool isInvocationTarget = false)
        {
            var handlerType = MatchingTypeFinder.TryFind(type, typeof(ArgumentHandlerProvider));
            if (handlerType != null)
                return CreateHandler(handlerType);

            if (isInvocationTarget)
                return ByRefArgumentHandler.Instance;
            var equatableType = typeof(IEquatable<>).MakeGenericType(type);
            if (equatableType.IsAssignableFrom(type)) {
                var eacType = typeof(EquatableArgumentHandler<>).MakeGenericType(type);
                var eac = (EquatableArgumentHandler) CreateHandler(eacType);
                if (eac.IsAvailable)
                    return eac;
            }
            return ArgumentHandler.Default;
        }

        protected virtual ArgumentHandler CreateHandler(Type comparerType)
        {
            var pInstance = comparerType.GetProperty(
                nameof(ByRefArgumentHandler.Instance),
                BindingFlags.Static | BindingFlags.Public);
            if (pInstance != null)
                return (ArgumentHandler) pInstance.GetValue(null);

            var fInstance = comparerType.GetField(
                nameof(ByRefArgumentHandler.Instance),
                BindingFlags.Static | BindingFlags.Public);
            if (fInstance != null)
                return (ArgumentHandler) fInstance.GetValue(null);

            return (ArgumentHandler) ServiceProvider.Activate(comparerType);
        }
    }
}
