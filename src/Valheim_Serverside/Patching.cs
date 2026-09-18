using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PatchingLib
{
	public interface IPatchRequirement
	{
		string Name { get; }
		Func<bool> Checker { get; }
	}

	public class PatchRequirements
	{
		readonly Dictionary<string, Func<bool>> _requirements;

		public PatchRequirements()
		{
			_requirements = new Dictionary<string, Func<bool>>();
		}

		public PatchRequirements(Dictionary<string, Func<bool>> requirements)
		{
			_requirements = requirements;
		}

		public PatchRequirements AddRequirement(IPatchRequirement patchRequirement)
		{
			_requirements.Add(patchRequirement.Name, patchRequirement.Checker);
			return this;
		}

		public bool IsAllowed(string requirement_name)
		{
			_requirements.TryGetValue(requirement_name, out Func<bool> checker);
			if (checker != null)
			{
				return checker();
			}
			return false;
		}
	}

	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
	public class PatchRequiresAttribute : Attribute
	{
		public readonly string requirement_name;

		public PatchRequiresAttribute(string requirement_name)
		{
			this.requirement_name = requirement_name;
		}
	}

	public class HarmonyFeaturesPatcher
	{
		private readonly PatchRequirements _patchRequirements;

		public HarmonyFeaturesPatcher(PatchRequirements availableFeatures)
		{
			_patchRequirements = availableFeatures;
		}

		private static void VerifyRegistration(Type type, Harmony harmony)
		{
			// PatchClassProcessor.Patch returns replacement methods, not original targets.
			// Read Harmony's registry using the originals owned by this instance instead.
			var registered = harmony.GetPatchedMethods().Select(Harmony.GetPatchInfo)
				.Where(info => info != null)
				.SelectMany(info => info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers))
				.Where(patch => patch.owner == harmony.Id && patch.PatchMethod.DeclaringType == type)
				.Select(patch => patch.PatchMethod).ToList();
			string[] hookNames = { "Prefix", "Postfix", "Transpiler", "Finalizer" };
			var expected = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				.Where(method => hookNames.Contains(method.Name) || method.GetCustomAttributes(false)
					.Any(attribute => hookNames.Any(name => attribute.GetType().Name == "Harmony" + name))).ToList();
			if (expected.Count == 0 || expected.Any(method => !registered.Contains(method)))
				throw new InvalidOperationException($"Incomplete Harmony registration for {type.FullName}");
		}

		public void PatchAll(Type[] types, Harmony harmony_instance, bool verify = false)
		{
			foreach (Type type in types)
			{
				var attributes = type.GetCustomAttributes<PatchRequiresAttribute>().ToList();
				bool enabled = !attributes.Any(attribute => !_patchRequirements.IsAllowed(attribute.requirement_name));
				if (enabled)
				{
					ZLog.Log("Patching: " + type.ToString());
					new PatchClassProcessor(harmony_instance, type).Patch();
					if (verify) VerifyRegistration(type, harmony_instance);
				}
				else
				{
					ZLog.Log("Patch disabled: " + type.ToString());
					if (verify) throw new InvalidOperationException($"Required hook {type.FullName} was skipped by patch requirements");
				}
			}
		}
	}
}
