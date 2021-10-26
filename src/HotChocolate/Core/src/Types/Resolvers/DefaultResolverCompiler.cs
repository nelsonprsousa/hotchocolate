using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HotChocolate.Internal;
using HotChocolate.Resolvers.Expressions.Parameters;
using HotChocolate.Types.Input;
using HotChocolate.Utilities;
using static System.Linq.Expressions.Expression;
using static HotChocolate.Properties.TypeResources;
using static HotChocolate.Resolvers.ResolveResultHelper;
using static HotChocolate.Resolvers.SubscribeResultHelper;

#nullable enable

namespace HotChocolate.Resolvers
{
    /// <summary>
    /// This class provides some helper methods to compile resolvers for dynamic schemas.
    /// </summary>
    internal sealed class DefaultResolverCompiler : IResolverCompiler
    {
        private static readonly IParameterExpressionBuilder[] _empty =
            Array.Empty<IParameterExpressionBuilder>();
        private static readonly ParameterExpression _context =
            Parameter(typeof(IResolverContext), "context");
        private static readonly ParameterExpression _pureContext =
            Parameter(typeof(IPureResolverContext), "context");
        private static readonly MethodInfo _parent =
            typeof(IPureResolverContext).GetMethod(nameof(IPureResolverContext.Parent))!;
        private static readonly MethodInfo _resolver =
            typeof(IPureResolverContext).GetMethod(nameof(IPureResolverContext.Resolver))!;

        private readonly Dictionary<ParameterInfo, IParameterExpressionBuilder> _cache = new();
        private readonly List<IParameterExpressionBuilder> _parameterExpressionBuilders;
        private readonly ImplicitArgumentParameterExpressionBuilder _defaultExprBuilder = new();

        public DefaultResolverCompiler(
            IEnumerable<IParameterExpressionBuilder>? customParameterExpressionBuilders)
        {
            // explicit internal expression builders will be added first.
            var list = new List<IParameterExpressionBuilder>
            {
                new ParentParameterExpressionBuilder(),
                new ServiceParameterExpressionBuilder(),
                new ArgumentParameterExpressionBuilder(),
                new GlobalStateParameterExpressionBuilder(),
                new ScopedStateParameterExpressionBuilder(),
                new LocalStateParameterExpressionBuilder(),
                new EventMessageParameterExpressionBuilder(),
                new ScopedServiceParameterExpressionBuilder(),
            };

            if (customParameterExpressionBuilders is not null)
            {
                // then we will add custom parameter expression builder and
                // give the user a chance to override our implicit expression builder.
                list.AddRange(customParameterExpressionBuilders);
            }

            // then we add the internal implicit expression builder.
            list.Add(new DocumentParameterExpressionBuilder());
            list.Add(new CancellationTokenParameterExpressionBuilder());
            list.Add(new ResolverContextParameterExpressionBuilder());
            list.Add(new PureResolverContextParameterExpressionBuilder());
            list.Add(new SchemaParameterExpressionBuilder());
            list.Add(new SelectionParameterExpressionBuilder());
            list.Add(new FieldSyntaxParameterExpressionBuilder());
            list.Add(new ObjectTypeParameterExpressionBuilder());
            list.Add(new OperationParameterExpressionBuilder());
            list.Add(new FieldParameterExpressionBuilder());
            list.Add(new ClaimsPrincipalParameterExpressionBuilder());
            list.Add(new PathParameterExpressionBuilder());
            list.Add(new InputParameterExpressionBuilder());

            _parameterExpressionBuilders = list;
        }

        /// <inheritdoc />
        public FieldResolverDelegates CompileResolve<TResolver>(
            Expression<Func<TResolver, object?>> propertyOrMethod,
            Type? sourceType = null,
            IParameterExpressionBuilder[]? parameterExpressionBuilders = null)
        {
            if (propertyOrMethod is null)
            {
                throw new ArgumentNullException(nameof(propertyOrMethod));
            }

            MemberInfo member = propertyOrMethod.TryExtractMember();

            if (member is PropertyInfo or MethodInfo)
            {
                Type source = sourceType ?? typeof(TResolver);
                Type? resolver = sourceType is null ? typeof(TResolver) : null;
                return CompileResolve(member, source, resolver, parameterExpressionBuilders);
            }

            throw new ArgumentException(
                ObjectTypeDescriptor_MustBePropertyOrMethod,
                nameof(member));
        }

        /// <inheritdoc />
        public FieldResolverDelegates CompileResolve(
            LambdaExpression lambda,
            Type sourceType,
            Type? resolverType = null)
        {
            resolverType ??= sourceType;

            Expression owner = CreateResolverOwner(_context, sourceType, resolverType);
            Expression resolver = Invoke(lambda, owner);
            resolver = EnsureResolveResult(resolver, lambda.ReturnType);
            return new(Lambda<FieldResolverDelegate>(resolver, _context).Compile());
        }

        /// <inheritdoc />
        public FieldResolverDelegates CompileResolve(
            MemberInfo member,
            Type? sourceType = null,
            Type? resolverType = null,
            IParameterExpressionBuilder[]? parameterExpressionBuilders = null)
        {
            if (member is null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            FieldResolverDelegate resolver;
            PureFieldDelegate? pureResolver = null;

            sourceType ??= member.ReflectedType ?? member.DeclaringType!;
            resolverType ??= sourceType;

            if (member is MethodInfo { IsStatic: true } method)
            {
                resolver = CompileStaticResolver(method, parameterExpressionBuilders ?? _empty);
            }
            else
            {
                resolver = CreateResolver(
                    member,
                    sourceType,
                    resolverType,
                    parameterExpressionBuilders ?? _empty);

                pureResolver = TryCompilePureResolver(
                    member,
                    sourceType,
                    resolverType,
                    parameterExpressionBuilders ?? _empty);
            }

            return new(resolver, pureResolver);
        }

        /// <inheritdoc />
        public SubscribeResolverDelegate CompileSubscribe(
            MemberInfo member,
            Type? sourceType = null,
            Type? resolverType = null)
        {
            sourceType ??= member.ReflectedType ?? member.DeclaringType!;
            resolverType ??= sourceType;

            if (member is MethodInfo method)
            {
                ParameterInfo[] parameters = method.GetParameters();
                Expression owner = CreateResolverOwner(_context, sourceType, resolverType);
                Expression[] parameterExpr = CreateParameters(_context, parameters, _empty);
                Expression subscribeResolver = Call(owner, method, parameterExpr);
                subscribeResolver = EnsureSubscribeResult(subscribeResolver, method.ReturnType);
                return Lambda<SubscribeResolverDelegate>(subscribeResolver, _context).Compile();
            }

            throw new ArgumentException(
                DefaultResolverCompilerService_CompileSubscribe_OnlyMethodsAllowed,
                nameof(member));
        }

        /// <inheritdoc />
        public IEnumerable<ParameterInfo> GetArgumentParameters(
            ParameterInfo[] parameters,
            IParameterExpressionBuilder[]? parameterExpressionBuilders = null)
        {
            foreach (ParameterInfo parameter in parameters)
            {
                IParameterExpressionBuilder builder =
                    GetParameterExpressionBuilder(
                        parameter,
                        parameterExpressionBuilders ?? _empty);

                if (builder.Kind == ArgumentKind.Argument)
                {
                    yield return parameter;
                }
            }
        }

        private FieldResolverDelegate CompileStaticResolver(
            MethodInfo method,
            IParameterExpressionBuilder[] fieldParameterExpressionBuilders)
        {
            Expression[] parameters = CreateParameters(
                _context,
                method.GetParameters(),
                fieldParameterExpressionBuilders);
            Expression resolver = Call(method, parameters);
            resolver = EnsureResolveResult(resolver, method.ReturnType);
            return Lambda<FieldResolverDelegate>(resolver, _context).Compile();
        }

        private FieldResolverDelegate CreateResolver(
            MemberInfo member,
            Type source,
            Type resolverType,
            IParameterExpressionBuilder[] fieldParameterExpressionBuilders)
        {
            if (member is PropertyInfo property)
            {
                Expression owner = CreateResolverOwner(_context, source, resolverType);
                Expression propResolver = Property(owner, property);
                propResolver = EnsureResolveResult(propResolver, property.PropertyType);
                return Lambda<FieldResolverDelegate>(propResolver, _context).Compile();
            }

            if (member is MethodInfo method)
            {
                ParameterInfo[] parameters = method.GetParameters();
                Expression owner = CreateResolverOwner(_context, source, resolverType);
                Expression[] parameterExpr = CreateParameters(
                    _context,
                    parameters,
                    fieldParameterExpressionBuilders);
                Expression methodResolver = Call(owner, method, parameterExpr);
                methodResolver = EnsureResolveResult(methodResolver, method.ReturnType);
                return Lambda<FieldResolverDelegate>(methodResolver, _context).Compile();
            }

            throw new NotSupportedException(
                DefaultResolverCompilerService_CreateResolver_ArgumentValudationError);
        }

        private PureFieldDelegate? TryCompilePureResolver(
            MemberInfo member,
            Type source,
            Type resolver,
            IParameterExpressionBuilder[] fieldParameterExpressionBuilders)
        {
            if (member is PropertyInfo property && IsPureResolverResult(property.PropertyType))
            {
                Expression owner = CreateResolverOwner(_pureContext, source, resolver);
                Expression propertyResolver = Property(owner, property);

                if (property.PropertyType != typeof(object))
                {
                    propertyResolver = Convert(propertyResolver, typeof(object));
                }

                return Lambda<PureFieldDelegate>(propertyResolver, _pureContext).Compile();
            }

            if (member is MethodInfo method)
            {
                ParameterInfo[] parameters = method.GetParameters();

                if (IsPureResolver(method, parameters, fieldParameterExpressionBuilders))
                {
                    Expression owner = CreateResolverOwner(_pureContext, source, resolver);
                    Expression[] parameterExpr = CreateParameters(
                        _pureContext,
                        parameters,
                        fieldParameterExpressionBuilders);
                    Expression methodResolver = Call(owner, method, parameterExpr);

                    if (method.ReturnType != typeof(object))
                    {
                        methodResolver = Convert(methodResolver, typeof(object));
                    }

                    return Lambda<PureFieldDelegate>(methodResolver, _pureContext).Compile();
                }
            }

            return null;
        }

        private bool IsPureResolver(
            MethodInfo method,
            ParameterInfo[] parameters,
            IParameterExpressionBuilder[] fieldParameterExpressionBuilders)
        {
            if (!IsPureResolverResult(method.ReturnType))
            {
                return false;
            }

            foreach (ParameterInfo parameter in parameters)
            {
                IParameterExpressionBuilder builder =
                    GetParameterExpressionBuilder(parameter, fieldParameterExpressionBuilders);

                if (!builder.IsPure)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsPureResolverResult(Type resultType)
        {
            if (resultType == typeof(ValueTask<object>))
            {
                return false;
            }

            if (typeof(IExecutable).IsAssignableFrom(resultType) ||
                typeof(IQueryable).IsAssignableFrom(resultType) ||
                typeof(Task).IsAssignableFrom(resultType))
            {
                return false;
            }

            if (resultType.IsGenericType)
            {
                Type type = resultType.GetGenericTypeDefinition();
                if (type == typeof(ValueTask<>) ||
                    type == typeof(IAsyncEnumerable<>))
                {
                    return false;
                }
            }

            return true;
        }

        // Create an expression to get the resolver class instance.
        private static Expression CreateResolverOwner(
            ParameterExpression context,
            Type source,
            Type resolver)
        {
            MethodInfo resolverMethod = source == resolver
                ? _parent.MakeGenericMethod(source)
                : _resolver.MakeGenericMethod(resolver);
            return Call(context, resolverMethod);
        }

        private Expression[] CreateParameters(
            ParameterExpression context,
            ParameterInfo[] parameters,
            IParameterExpressionBuilder[] fieldParameterExpressionBuilders)
        {
            var parameterResolvers = new Expression[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];

                IParameterExpressionBuilder builder =
                    GetParameterExpressionBuilder(parameter, fieldParameterExpressionBuilders);

                parameterResolvers[i] = builder.Build(parameter, context);
            }

            return parameterResolvers;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IParameterExpressionBuilder GetParameterExpressionBuilder(
            ParameterInfo parameter,
            IParameterExpressionBuilder[] fieldParameterExpressionBuilders)
        {
            if (_cache.TryGetValue(parameter, out var cached))
            {
                return cached;
            }

            if (fieldParameterExpressionBuilders.Length > 0)
            {
                foreach (IParameterExpressionBuilder builder in fieldParameterExpressionBuilders)
                {
                    if (builder.CanHandle(parameter))
                    {
#if NETSTANDARD
                    _cache[parameter] = builder;
#else
                        _cache.TryAdd(parameter, builder);
#endif
                        return builder;
                    }
                }
            }

            foreach (IParameterExpressionBuilder builder in _parameterExpressionBuilders)
            {
                if (builder.CanHandle(parameter))
                {
#if NETSTANDARD
                    _cache[parameter] = builder;
#else
                    _cache.TryAdd(parameter, builder);
#endif
                    return builder;
                }
            }

            return _defaultExprBuilder;
        }

        public void Dispose()
        {
            _cache.Clear();
        }
    }
}
