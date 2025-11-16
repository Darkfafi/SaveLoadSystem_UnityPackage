using RasofiaGames.SaveLoadSystem.Internal.Utils;
using System;

namespace RasofiaGames.SaveLoadSystem.Internal
{
	[Serializable]
	public struct SaveableValueSection
	{
		public string ValueString;
		public string ValueType;

		public SaveableValueSection(object value, Type specifiedType = null)
		{
			if (specifiedType == null)
			{
				specifiedType = value.GetType();
			}
			
			ValueString = PrimitiveToValueParserUtility.ToJSON(value, specifiedType);
			ValueType = specifiedType.AssemblyQualifiedName;
		}

		public bool IsValid
		{
			get
			{
				return !string.IsNullOrEmpty(ValueType);
			}
		}

		public object GetValue(Type specifiedType)
		{
			return PrimitiveToValueParserUtility.FromJSON(ValueString, specifiedType);
		}

		public T GetValue<T>()
		{
			return (T)PrimitiveToValueParserUtility.FromJSON(ValueString, typeof(T));
		}

		public object GetValue()
		{
			return GetValue(GetValueType());
		}

		public Type GetValueType()
		{
			return Type.GetType(ValueType);
		}

		public Type GetSafeValueType()
		{
			if (string.IsNullOrEmpty(ValueType))
				return null;

			try
			{
				return Type.GetType(ValueType);
			}
			catch
			{
				return null;
			}
		}
	}
}