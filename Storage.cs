﻿using RasofiaGames.SaveLoadSystem.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace RasofiaGames.SaveLoadSystem
{
	[StorageKeysHolder(typeof(ISaveable))]
	public class Storage : IStorageAccess
	{
		public enum EncodingType
		{
			None,
			Base64,
		}

		[StorageKey(typeof(string))]
		public const string STORAGE_REFERENCE_TYPE_STRING_KEY = "RESERVED_REFERENCE_TYPE_FULL_NAME_STRING_RESERVED";

		[StorageKey(typeof(ulong))]
		public const string STORAGE_REFERENCE_TYPE_ID_ULONG_KEY = "RESERVED_REFERENCE_TYPE_ID_ULONG_RESERVED";

		public const string ROOT_SAVE_DATA_CAPSULE_REFERENCE_ID = "ID_CAPSULE_SAVE_DATA";
		public const string SAVE_FILE_EXTENSION = "rdpsf";

		public const string STORAGE_OBJECT_FACTORY_TYPE_NAME = "StorageObjectFactory";

		public SaveableReferenceIdHandler ActiveRefHandler
		{
			get; private set;
		}

		public string StorageLocationPath
		{
			get; private set;
		}

		public EncodingType EncodingOption
		{
			get; private set;
		}

		private Dictionary<IStorageCapsule, Dictionary<string, StorageDictionary>> _cachedStorageCapsules = new Dictionary<IStorageCapsule, Dictionary<string, StorageDictionary>>();
		private IStorageObjectFactory _storageObjectFactory;

		public static string GetPathToStorageCapsule(string locationPath, IStorageCapsule capsule, bool addFileType)
		{
			return Path.Combine(Application.persistentDataPath, string.Concat(locationPath, capsule.ID + (addFileType ? "." + SAVE_FILE_EXTENSION : "")));
		}

		public static string GetPathToStorage(string locationPath)
		{
			return Path.Combine(Application.persistentDataPath, locationPath);
		}

		public static Type GetStorageFactoryType()
		{
			return AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes()).FirstOrDefault(x => x.Name == STORAGE_OBJECT_FACTORY_TYPE_NAME);
		}

		public Storage(string storageLocationPath, EncodingType encodingType, params IStorageCapsule[] allStorageCapsules)
		{
			_storageObjectFactory = Activator.CreateInstance(GetStorageFactoryType()) as IStorageObjectFactory;
			StorageLocationPath = storageLocationPath;
			EncodingOption = encodingType;

			_cachedStorageCapsules.Clear();
			for(int i = 0, c = allStorageCapsules.Length; i < c; i++)
			{
				_cachedStorageCapsules.Add(allStorageCapsules[i], new Dictionary<string, StorageDictionary>());
				RefreshCachedData(allStorageCapsules[i]);
			}
		}

		public void Load(params string[] storageCapsuleIDs)
		{
			using(ActiveRefHandler = new SaveableReferenceIdHandler())
			{
				RefreshCachedData(storageCapsuleIDs);

				foreach(var capsuleToStorage in _cachedStorageCapsules)
				{

					if(storageCapsuleIDs != null && storageCapsuleIDs.Length > 0 && Array.IndexOf(storageCapsuleIDs, capsuleToStorage.Key.ID) < 0)
						continue;

					List<ISaveable> _allLoadedReferences = new List<ISaveable>();
					List<string> _allLoadedReferenceIds = new List<string>();

					Action<string> referenceRequestedEventAction = (id) =>
					{
						if(_allLoadedReferenceIds.Contains(id))
							return;

						_allLoadedReferenceIds.Add(id);

						if(!capsuleToStorage.Value.TryGetValue(id, out StorageDictionary storage))
						{
							storage = new StorageDictionary(capsuleToStorage.Key.ID, this);
						}

						if(id == ROOT_SAVE_DATA_CAPSULE_REFERENCE_ID)
						{
							capsuleToStorage.Key.Load(storage);
							_allLoadedReferences.Add(capsuleToStorage.Key);
						}
						else if(storage.LoadValue(STORAGE_REFERENCE_TYPE_ID_ULONG_KEY, out ulong classTypeId))
						{
							ISaveable referenceInstance = _storageObjectFactory.LoadSaveableObject(classTypeId, storage);
							ActiveRefHandler.SetReferenceReady(referenceInstance, id);
							_allLoadedReferences.Add(referenceInstance);
						}
						else if(storage.LoadValue(STORAGE_REFERENCE_TYPE_STRING_KEY, out string classTypeFullName))
						{
							Type referenceType = Type.GetType(classTypeFullName);
							bool methodLoadInterface = typeof(ISaveableLoad).IsAssignableFrom(referenceType);
							ISaveable referenceInstance = (methodLoadInterface ? Activator.CreateInstance(referenceType) : Activator.CreateInstance(referenceType, storage)) as ISaveable;
							ActiveRefHandler.SetReferenceReady(referenceInstance, id);

							if(methodLoadInterface)
								((ISaveableLoad)referenceInstance).Load(storage);

							_allLoadedReferences.Add(referenceInstance);
						}
						else
						{
							Debug.LogErrorFormat("UNABLE TO LOAD REFERENCE ID {0}'s CLASS TYPE NAME", id);
						}
					};

					ActiveRefHandler.ReferenceRequestedEvent += referenceRequestedEventAction;
					referenceRequestedEventAction(ROOT_SAVE_DATA_CAPSULE_REFERENCE_ID);
					ActiveRefHandler.LoadRemainingAsNull();
					ActiveRefHandler.ReferenceRequestedEvent -= referenceRequestedEventAction;

					for(int i = _allLoadedReferences.Count - 1; i >= 0; i--)
					{
						_allLoadedReferences[i].LoadingCompleted();
					}

					_allLoadedReferences = null;
					_allLoadedReferenceIds = null;
				}
			}
		}

		public void Save(bool flushAfterSave, params string[] storageCapsuleIDs)
		{
			Dictionary<IStorageCapsule, Dictionary<string, StorageDictionary>> buffer = new Dictionary<IStorageCapsule, Dictionary<string, StorageDictionary>>();
			Dictionary<string, IStorageCapsule> _alreadySavedReferencesToOriginCapsuleMap = new Dictionary<string, IStorageCapsule>();
			using(ActiveRefHandler = new SaveableReferenceIdHandler())
			{
				foreach(var pair in _cachedStorageCapsules)
				{
					if(storageCapsuleIDs != null && storageCapsuleIDs.Length > 0 && Array.IndexOf(storageCapsuleIDs, pair.Key.ID) < 0)
						continue;

					Dictionary<string, StorageDictionary> referencesSaved = new Dictionary<string, StorageDictionary>();

					Action<string, ISaveable> refDetectedAction = (refID, referenceInstance) =>
					{
						if(_alreadySavedReferencesToOriginCapsuleMap.TryGetValue(refID, out IStorageCapsule holdingCapsule))
						{
							if(holdingCapsule != pair.Key)
							{
								throw new Exception(string.Format("Save aborted! Reference {0} saved in capsule {1} while capsule {2} is saving it now! Each capsule should not be saving cross references!", referenceInstance.ToString(), holdingCapsule.ID, pair.Key.ID));
							}
						}

						if(!referencesSaved.ContainsKey(refID))
						{
							StorageDictionary storageDictForRef = new StorageDictionary(pair.Key.ID, this);
							referencesSaved.Add(refID, storageDictForRef);
							storageDictForRef.SaveValue(STORAGE_REFERENCE_TYPE_STRING_KEY, referenceInstance.GetType().AssemblyQualifiedName);
							storageDictForRef.SaveValue(STORAGE_REFERENCE_TYPE_ID_ULONG_KEY, _storageObjectFactory.GetIdForSaveable(referenceInstance.GetType()));
							referenceInstance.Save(storageDictForRef);

							if(pair.Value.TryGetValue(refID, out StorageDictionary oldData))
							{
								foreach(var valueKey in oldData.GetValueStorageKeys())
								{
									if(oldData.ShouldKeepValueKey(valueKey) && !storageDictForRef.HasValueKey(valueKey))
									{
										storageDictForRef.SetValue(valueKey, oldData.GetValueSection(valueKey).GetValue());
									}
								}

								foreach(var refKey in oldData.GetRefStorageKeys())
								{
									if(oldData.ShouldKeepRefKey(refKey) && !storageDictForRef.HasRefKey(refKey))
									{
										storageDictForRef.SetValueRef(refKey, oldData.GetValueRef(refKey));
									}
								}
							}

							if(refID != ROOT_SAVE_DATA_CAPSULE_REFERENCE_ID)
								_alreadySavedReferencesToOriginCapsuleMap.Add(refID, pair.Key);
						}
					};

					ActiveRefHandler.IdForReferenceRequestedEvent += refDetectedAction;
					refDetectedAction(ROOT_SAVE_DATA_CAPSULE_REFERENCE_ID, pair.Key);
					ActiveRefHandler.IdForReferenceRequestedEvent -= refDetectedAction;

					buffer.Add(pair.Key, referencesSaved);
				}
			}

			foreach(var pair in buffer)
			{
				_cachedStorageCapsules[pair.Key] = pair.Value;
			}

			if(flushAfterSave)
				Flush();
		}

		public bool TryRead(string storageCapsuleID, out ReadStorageResult readStorageResult)
		{
			List<ReadStorageResult> storages = Read(new string[] { storageCapsuleID });

			if(storages.Count > 0)
			{
				readStorageResult = storages[0];
				return true;
			}

			readStorageResult = default;
			return false;
		}

		public List<ReadStorageResult> Read(params string[] storageCapsuleIDs)
		{
			List<ReadStorageResult> storageDicts = new List<ReadStorageResult>();

			RefreshCachedData(storageCapsuleIDs);

			foreach(var capsuleToStorage in _cachedStorageCapsules)
			{
				if(storageCapsuleIDs == null || storageCapsuleIDs.Length == 0 || Array.IndexOf(storageCapsuleIDs, capsuleToStorage.Key.ID) >= 0)
				{
					List<KeyValuePair<Type, IStorageDictionaryEditor>> refStorages = new List<KeyValuePair<Type, IStorageDictionaryEditor>>();
					if(capsuleToStorage.Value.TryGetValue(ROOT_SAVE_DATA_CAPSULE_REFERENCE_ID, out StorageDictionary capsuleStorage))
					{
						foreach(var storageItem in capsuleToStorage.Value)
						{
							if(storageItem.Key != ROOT_SAVE_DATA_CAPSULE_REFERENCE_ID)
							{
								Type referenceType = null;

								if(storageItem.Value.LoadValue(STORAGE_REFERENCE_TYPE_ID_ULONG_KEY, out ulong classTypeId))
								{
									referenceType = _storageObjectFactory.GetTypeForId(classTypeId);
								}
								else if(storageItem.Value.LoadValue(STORAGE_REFERENCE_TYPE_STRING_KEY, out string referenceTypeString))
								{
									referenceType = Type.GetType(referenceTypeString);
								}

								if(referenceType != null)
								{
									refStorages.Add(new KeyValuePair<Type, IStorageDictionaryEditor>(referenceType, storageItem.Value));
								}
							}
						}
					}
					else
					{
						capsuleStorage = new StorageDictionary(capsuleToStorage.Key.ID, this);
						capsuleToStorage.Value.Add(ROOT_SAVE_DATA_CAPSULE_REFERENCE_ID, capsuleStorage);
					}

					storageDicts.Add(new ReadStorageResult(capsuleToStorage.Key.ID, capsuleStorage, refStorages));
				}
			}

			return storageDicts;
		}

		public void Clear(bool removeSaveFiles, params string[] storageCapsuleIDs)
		{
			Dictionary<IStorageCapsule, Dictionary<string, StorageDictionary>> buffer = new Dictionary<IStorageCapsule, Dictionary<string, StorageDictionary>>();
			foreach(var pair in _cachedStorageCapsules)
			{
				if(storageCapsuleIDs == null || storageCapsuleIDs.Length == 0 || Array.IndexOf(storageCapsuleIDs, pair.Key.ID) >= 0)
				{
					buffer.Add(pair.Key, new Dictionary<string, StorageDictionary>());
				}
			}

			if(!Directory.Exists(GetPathToStorage(StorageLocationPath)))
				return;

			foreach(var pair in buffer)
			{
				if(removeSaveFiles)
				{
					string pathToFile = GetPathToStorageCapsule(StorageLocationPath, pair.Key, true);
					if(File.Exists(pathToFile))
					{
						File.Delete(pathToFile);
					}
				}
				else
				{
					_cachedStorageCapsules[pair.Key] = pair.Value;
				}
			}

			if(Directory.GetFiles(GetPathToStorage(StorageLocationPath)).Length == 0)
				Directory.Delete(GetPathToStorage(StorageLocationPath));

			if(!removeSaveFiles)
				Flush(storageCapsuleIDs);
		}

		public void Flush(params string[] storageCapsuleIDs)
		{
			foreach(var capsuleMapItem in _cachedStorageCapsules)
			{
				if(storageCapsuleIDs != null && storageCapsuleIDs.Length > 0 && Array.IndexOf(storageCapsuleIDs, capsuleMapItem.Key) >= 0)
					continue;

				if(capsuleMapItem.Value != null)
				{
					List<SaveDataForReference> sectionsForReferences = new List<SaveDataForReference>();

					foreach(var pair in capsuleMapItem.Value)
					{
						sectionsForReferences.Add(new SaveDataForReference()
						{
							ReferenceID = pair.Key,
							ValueDataItems = pair.Value.GetValueDataItems(),
							ReferenceDataItems = pair.Value.GetReferenceDataItems(),
						});
					}

					string jsonString = JsonUtility.ToJson(new SaveData()
					{
						CapsuleID = capsuleMapItem.Key.ID,
						ReferencesSaveData = sectionsForReferences.ToArray(),
					});


					try
					{
						string pathToStorage = GetPathToStorage(StorageLocationPath);
						if(!Directory.Exists(pathToStorage))
						{
							Directory.CreateDirectory(pathToStorage);
						}

						using(StreamWriter writer = new StreamWriter(GetPathToStorageCapsule(StorageLocationPath, capsuleMapItem.Key, true)))
						{
							writer.Write(Encode(JsonUtility.ToJson(new SaveFileWrapper()
							{
								SafeFileText = jsonString,
								SaveFilePassword = GetEncryptionPassword(jsonString)
							})));
						}
					}
					catch(Exception e)
					{
						throw new Exception(string.Format("Could not save file {0}. Error: {1}", capsuleMapItem.Key, e.Message));
					}
				}
			}
		}

		public EditableRefValue GetEditableRefValue(string storageCapsuleID, string refID)
		{
			if(string.IsNullOrEmpty(refID))
			{
				return default;
			}

			foreach(var item in _cachedStorageCapsules)
			{
				if(item.Key.ID == storageCapsuleID)
				{
					if(item.Value.TryGetValue(refID, out StorageDictionary storageForRef))
					{
						if(storageForRef.LoadValue(STORAGE_REFERENCE_TYPE_ID_ULONG_KEY, out ulong classTypeId))
						{
							return new EditableRefValue(refID, _storageObjectFactory.GetTypeForId(classTypeId).AssemblyQualifiedName, storageForRef);
						}

						if(storageForRef.LoadValue(STORAGE_REFERENCE_TYPE_STRING_KEY, out string referenceTypeString))
						{
							return new EditableRefValue(refID, referenceTypeString, storageForRef);
						}
					}
					break;
				}
			}

			return default;
		}

		public EditableRefValue RegisterNewRefInCapsule(string storageCapsuleID, Type referenceType)
		{
			IStorageCapsule capsuleToEdit = null;
			EditableRefValue editableRefValue = default;

			foreach(var item in _cachedStorageCapsules)
			{
				if(item.Key.ID == storageCapsuleID)
				{
					StorageDictionary storageForRef = new StorageDictionary(storageCapsuleID, this);
					storageForRef.SaveValue(STORAGE_REFERENCE_TYPE_STRING_KEY, referenceType.AssemblyQualifiedName);
					storageForRef.SaveValue(STORAGE_REFERENCE_TYPE_ID_ULONG_KEY, _storageObjectFactory.GetIdForSaveable(referenceType));
					string randomOnFlyID = Guid.NewGuid().ToString("N");
					editableRefValue = new EditableRefValue(randomOnFlyID, referenceType.AssemblyQualifiedName, storageForRef);
					capsuleToEdit = item.Key;
					break;
				}
			}

			if(editableRefValue.IsValidRefValue && capsuleToEdit != null)
			{
				_cachedStorageCapsules[capsuleToEdit].Add(editableRefValue.ReferenceID, editableRefValue.Storage as StorageDictionary);
			}

			return editableRefValue;
		}

		private void RefreshCachedData(string[] capsuleIDs)
		{
			List<IStorageCapsule> capsules = new List<IStorageCapsule>();
			foreach(var pair in _cachedStorageCapsules)
			{
				if(Array.IndexOf(capsuleIDs, pair.Key.ID) >= 0)
				{
					capsules.Add(pair.Key);
				}
			}

			for(int i = 0; i < capsules.Count; i++)
			{
				RefreshCachedData(capsules[i]);
			}
		}

		private void RefreshCachedData(IStorageCapsule capsuleToLoad)
		{
			SaveData saveDataForCapsule = LoadFromDisk(capsuleToLoad);

			Dictionary<string, StorageDictionary> referencesSaveData = new Dictionary<string, StorageDictionary>();

			if(saveDataForCapsule.ReferencesSaveData != null)
			{
				for(int i = 0, c = saveDataForCapsule.ReferencesSaveData.Length; i < c; i++)
				{
					SaveDataForReference refData = saveDataForCapsule.ReferencesSaveData[i];
					referencesSaveData.Add(refData.ReferenceID, new StorageDictionary(capsuleToLoad.ID, this, SaveDataItem.ToDictionary(refData.ValueDataItems), SaveDataItem.ToObjectDictionary(refData.ReferenceDataItems)));
				}
			}

			_cachedStorageCapsules[capsuleToLoad] = referencesSaveData;
		}

		private SaveData LoadFromDisk(IStorageCapsule capsuleToLoad)
		{
			string path = GetPathToStorageCapsule(StorageLocationPath, capsuleToLoad, true);
			if(File.Exists(path))
			{
				using(StreamReader reader = File.OpenText(path))
				{
					string jsonString = reader.ReadToEnd();
					SaveFileWrapper saveFileWrapper = JsonUtility.FromJson<SaveFileWrapper>(Decode(jsonString));
					if(ValidateEncryptionPassword(saveFileWrapper.SaveFilePassword, saveFileWrapper.SafeFileText))
					{
						return JsonUtility.FromJson<SaveData>(saveFileWrapper.SafeFileText);
					}
					else
					{
						Debug.Log("SAVE FILE IS CORRUPT, NEW SAVE FILE CREATED!");
					}
				}
			}

			return new SaveData()
			{
				CapsuleID = capsuleToLoad.ID,
			};
		}

		private string Encode(string text)
		{
			switch(EncodingOption)
			{
				case EncodingType.None:
					return text;
				case EncodingType.Base64:
					return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
				default:
					Debug.LogErrorFormat("Encryption type {0} not supported!", EncodingOption);
					return text;
			}
		}

		private string Decode(string text)
		{
			switch(EncodingOption)
			{
				case EncodingType.None:
					return text;
				case EncodingType.Base64:
					return Encoding.UTF8.GetString(Convert.FromBase64String(text));
				default:
					Debug.LogErrorFormat("Decryption type {0} not supported!", EncodingOption);
					return text;
			}
		}

		private string GetEncryptionPassword(string fileText)
		{
			HashAlgorithm algorithm = MD5.Create();
			List<byte> bytes = new List<byte>(Encoding.UTF8.GetBytes(Encode(fileText)));
			bytes.AddRange(Encoding.UTF8.GetBytes(fileText));

			StringBuilder sb = new StringBuilder();
			foreach(byte b in algorithm.ComputeHash(bytes.ToArray()))
				sb.Append(b.ToString("X2"));

			return Encode(sb.ToString());
		}

		private bool ValidateEncryptionPassword(string password, string fileText)
		{
			return password == GetEncryptionPassword(fileText);
		}
	}

	public interface IStorageCapsule : ISaveableLoad
	{
		string ID
		{
			get;
		}
	}

	public interface IStorageAccess : IEditableStorage
	{
		SaveableReferenceIdHandler ActiveRefHandler
		{
			get;
		}
	}
}