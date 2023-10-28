﻿using RasofiaGames.SaveLoadSystem.Internal.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RasofiaGames.SaveLoadSystem.Internal
{
	[SerializeField]
	public struct SaveFileWrapper
	{
		public string SaveFilePassword;
		public string SafeFileText;
	}

	[Serializable]
	public struct SaveData
	{
		public string CapsuleID;
		public SaveDataForReference[] ReferencesSaveData;
	}

	[Serializable]
	public struct SaveDataForReference
	{
		public string ReferenceID;
		public SaveDataItem[] ValueDataItems;
		public SaveDataItem[] ReferenceDataItems;
	}

	[Serializable]
	public struct SaveDataItem
	{
		public string SectionKey;
		public SaveableValueSection ValueSection;

		public SaveDataItem(string key, object value)
		{
			SectionKey = key;
			ValueSection = new SaveableValueSection(value);
		}

		public static SaveDataItem CreateFrom(string key, SaveableValueSection saveableValue)
		{
			return new SaveDataItem()
			{
				SectionKey = key,
				ValueSection = saveableValue,
			};
		}

		public static Dictionary<string, object> ToObjectDictionary(SaveDataItem[] itemsCollection)
		{
			Dictionary<string, object> returnValue = new Dictionary<string, object>();

			for(int i = 0, c = itemsCollection.Length; i < c; i++)
			{
				returnValue.Add(itemsCollection[i].SectionKey, itemsCollection[i].ValueSection.GetValue());
			}

			return returnValue;
		}

		public static Dictionary<string, SaveableValueSection> ToDictionary(SaveDataItem[] itemsCollection)
		{
			Dictionary<string, SaveableValueSection> returnValue = new Dictionary<string, SaveableValueSection>();

			for(int i = 0, c = itemsCollection.Length; i < c; i++)
			{
				returnValue.Add(itemsCollection[i].SectionKey, itemsCollection[i].ValueSection);
			}

			return returnValue;
		}
	}
}