using System;

namespace RasofiaGames.SaveLoadSystem.Internal
{
	public interface IStorageObjectFactory
	{
		ISaveable CreateSaveableObject(ulong id);
		Type GetTypeForId(ulong id);
		ulong GetIdForSaveable<T>() where T : ISaveable;
		ulong GetIdForSaveable(Type type);
	}
}