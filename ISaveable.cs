using System;

namespace RasofiaGames.SaveLoadSystem
{
	public interface ISaveable : IDisposable
	{
		StorageChannel GetStorageChannel();
	}

	public class StorageChannel : IDisposable
	{
		public delegate void SaveHandler(IStorageSaver saver);
		public delegate void LoadHandler(IStorageLoader loader);
		public delegate void LoadCompletedHandler();

		private StorageDictionary _lastStorageDictionary = null;

		private SaveHandler _saver = null;
		private LoadHandler _loader = null;
		private LoadCompletedHandler _loaded = null;

		public StorageChannel(SaveHandler saver, LoadHandler loader, LoadCompletedHandler loaded = null)
		{
			_saver = saver;
			_loader = loader;
			_loaded = loaded;
		}

		internal void Internal_Save(StorageDictionary saver)
		{
			_lastStorageDictionary = saver;
			_saver?.Invoke(saver);
		}

		internal void Internal_Load(StorageDictionary loader)
		{
			_lastStorageDictionary = loader;
			_loader?.Invoke(loader);
		}

		internal void Internal_Loaded()
		{
			_loaded?.Invoke();
		}

		internal bool Internal_TryGetLastStorageDictionary(out StorageDictionary storageDictionary)
		{
			storageDictionary = _lastStorageDictionary;
			return storageDictionary != null;
		}

		public void Dispose()
		{
			_saver = default;
			_loader = default;
			_loaded = default;
			_lastStorageDictionary = default;
		}
	}
}