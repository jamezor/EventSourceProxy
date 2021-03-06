﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EventSourceProxy
{
	/// <summary>
	/// Implements a provider that can bundle the parameters of an interface.
	/// </summary>
	public class TraceParameterProvider
	{
		#region Fields and Properties
		/// <summary>
		/// The default parameter provider.
		/// </summary>
		private static TraceParameterProvider _defaultProvider = new TraceParameterProvider();

		/// <summary>
		/// The list of parameter builders.
		/// </summary>
		private List<ParameterBuilder> _builders = new List<ParameterBuilder>();

		/// <summary>
		/// Gets the default TraceParameterProvider.
		/// </summary>
		public static TraceParameterProvider Default { get { return _defaultProvider; } }

		/// <summary>
		/// Gets the list of TraceBuilders that have been defined for this provider.
		/// </summary>
		internal IEnumerable<ParameterBuilder> Builders { get { return _builders; } }
		#endregion

		#region Fluent Configuration Methods
		/// <summary>
		/// Configures a parameter rule that matches all types and methods.
		/// </summary>
		/// <returns>A configuration rule that can be extended.</returns>
		public IParameterBuilder ForAnything()
		{
			return new ParameterBuilder(this);
		}

		/// <summary>
		/// Configures a parameter rule that matches the given type.
		/// </summary>
		/// <typeparam name="TSource">The type to be matched.</typeparam>
		/// <returns>A configuration rule that can be extended.</returns>
		[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter")]
		public IParameterBuilder For<TSource>()
		{
			return new ParameterBuilder<TSource>(this);
		}

		/// <summary>
		/// Configures a parameter rule that matches the given type.
		/// </summary>
		/// <typeparam name="TSource">The type to be matched.</typeparam>
		/// <param name="methodExpression">A lambda expression that contains a method call to match on.</param>
		/// <returns>A configuration rule that can be extended.</returns>
		[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1006:DoNotNestGenericTypesInMemberSignatures")]
		public IParameterBuilder For<TSource>(Expression<Action<TSource>> methodExpression)
		{
			return new ParameterBuilder<TSource>(this, methodExpression);
		}
		#endregion

		/// <summary>
		/// Returns the parameter mapping for the given method.
		/// </summary>
		/// <param name="methodInfo">The method to analyze.</param>
		/// <returns>A list of ParameterMapping representing the desired bundling of parameters.</returns>
		public virtual IReadOnlyCollection<ParameterMapping> ProvideParameterMapping(MethodInfo methodInfo)
		{
			if (methodInfo == null) throw new ArgumentNullException("methodInfo");

			return EvaluateBuilders(methodInfo);
		}

		/// <summary>
		/// Returns the parameter provider for a given type.
		/// </summary>
		/// <param name="interfaceType">The type to analyze.</param>
		/// <returns>The parameter provider for the type.</returns>
		internal static TraceParameterProvider GetParameterProvider(Type interfaceType)
		{
			return ProviderManager.GetProvider<TraceParameterProvider>(interfaceType, typeof(TraceParameterProviderAttribute), () => _defaultProvider);
		}

		/// <summary>
		/// Adds a TraceBuilder rule to this provider.
		/// </summary>
		/// <param name="builder">The ParameterBuilder to add.</param>
		internal void Add(ParameterBuilder builder)
		{
			if (!_builders.Contains(builder))
				_builders.Add(builder);
		}

		#region Internal Methods for Building
		/// <summary>
		/// Adds a mapping to the list of mappings
		/// </summary>
		/// <param name="mappings">The list of parameter mappings.</param>
		/// <param name="parameterInfo">The parameter being evaluated.</param>
		/// <param name="parameterName">The name of the parameter.</param>
		/// <param name="alias">The alias to use to output the parameter.</param>
		/// <param name="converter">An optional expression to use to convert the parameter.</param>
		private static void AddMapping(List<ParameterMapping> mappings, ParameterInfo parameterInfo, string parameterName, string alias, LambdaExpression converter)
		{
			// find the mapping that matches the name or create a new mapping
			var mapping = mappings.FirstOrDefault(p => String.Compare(p.Name, parameterName, StringComparison.OrdinalIgnoreCase) == 0);
			if (mapping == null)
			{
				mapping = new ParameterMapping(parameterName);
				mappings.Add(mapping);
			}

			mapping.AddSource(parameterInfo, alias, converter);
		}

		/// <summary>
		/// Uses the TraceBuilder rules on the provider to generate the parameter bindings.
		/// </summary>
		/// <param name="methodInfo">The method to analyze.</param>
		/// <returns>A list of ParameterMapping representing the desired bundling of parameters.</returns>
		private IReadOnlyCollection<ParameterMapping> EvaluateBuilders(MethodInfo methodInfo)
		{
			var mappings = new List<ParameterMapping>();
			var parameters = methodInfo.GetParameters().ToList();

			// give a context rule the opportunity to bind
			parameters.Add(null);

			// get the default name to use for tracing (null if none specified)
			var traceAsDefault = methodInfo.GetCustomAttribute<TraceAsAttribute>();

			foreach (var parameter in parameters)
			{
				bool hasRule = false;

				// go through the rules
				foreach (var builder in _builders.Where(b => b.Matches(methodInfo)))
				{
					foreach (var value in builder.Values.Where(v => v.Matches(parameter)))
					{
						hasRule = true;

						// if the value is an ignore rule, skip it
						if (value.Ignore)
							continue;

						// add the parameter value
						AddMapping(mappings, parameter, builder.Alias ?? "data", value.Alias ?? builder.Alias ?? "data", value.Converter);
					}
				}

				// if a rule has been applied, then don't add the base value
				if (hasRule)
					continue;

				if (parameter == null)
					continue;

				// if there is a TraceIgnore attribute, then skip this parameter
				if (parameter.GetCustomAttribute<TraceIgnoreAttribute>() != null)
					continue;

				// we need one parameter per attribute, and at least one per parameter
				var attributes = parameter.GetCustomAttributes<TraceAsAttribute>();
				if (!attributes.Any())
					attributes = new TraceAsAttribute[1] { traceAsDefault ?? new TraceAsAttribute(parameter.Name) };

				foreach (var attribute in attributes)
				{
					var traceName = attribute.Name;

					// if the attribute is a TraceMember, then create an expression to get the member
					LambdaExpression expression = null;
					var traceMember = attribute as TraceMemberAttribute;
					if (traceMember != null)
					{
						var input = Expression.Parameter(parameter.ParameterType);
						expression = Expression.Lambda(
							Expression.MakeMemberAccess(
								input,
								parameter.ParameterType.GetMember(traceMember.Member).First()),
							input);
					}

					AddMapping(mappings, parameter, traceName, parameter.Name, expression);
				}
			}

			return mappings;
		}
		#endregion
	}
}
