﻿#region ================= Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using mxd.SQL2.DataReaders;
using mxd.SQL2.Items;
using mxd.SQL2.Tools;

#endregion

namespace mxd.SQL2.Games
{
	public abstract class GameHandler
	{
		#region ================= Variables

		protected string defaultmodpath; // id1 / baseq2 / data1 etc.
		protected string gamepath; // c:\games\Quake, c:\games\Quake2 etc.
		protected string modname; // xatrix / Arcane Dimensions / yoursupermod etc.
		protected string ignoredmapprefix; // map filenames starting with this will be ignored
		protected HashSet<string> supporteddemoextensions; // .dem, .mvd, .qvd etc.
		protected Dictionary<string, GameItem> basegames; // Quake-specific game flags; <folder name, BaseGameItem>
		protected List<SkillItem> skills; // Easy, Medium, Hard, Nightmare!
		protected List<ClassItem> classes; // Cleric, Paladin, Necromancer [default], Assassin, Demoness [Hexen 2 only]
		protected Dictionary<bool, string> fullscreenarg; // true: arg to run the game in fullscreen, false: arg to run the game windowed

		private HashSet<string> mapnames;
		private HashSet<string> defaultmapnames; // Map names from defaultmodpath. Needed to properly check demos MapPath data... 
		private Dictionary<string, MapItem> defaultmaplist; // Maps from defaultmodpath. Needed to populate "maps" dropdown for mods without maps...
		private List<string> skillnames;
		private List<string> classnames; // [Hexen 2 only]

		private static string supportedgames; // "Quake / Quake II / Hexen 2 / Half-Life"
		private static GameHandler current;

		// Command line
		protected Dictionary<ItemType, string> launchparams; 

		#endregion

		#region ================= Properties

		public abstract string GameTitle { get; } // Quake / Quake II / Hexen 2 / Half-Life etc.
		public Dictionary<ItemType, string> LaunchParameters => launchparams;
		public string DefaultModPath => defaultmodpath; // c:\games\Quake\ID1, c:\games\Quake2\baseq2 etc.
		public string GamePath => gamepath;
		public string IgnoredMapPrefix => ignoredmapprefix;
		public HashSet<string> SupportedDemoExtensions => supporteddemoextensions;
		public ICollection<GameItem> BaseGames => basegames.Values;
		public List<SkillItem> Skills => skills;
		public List<ClassItem> Classes => classes;
		public Dictionary<bool, string> FullScreenArg => fullscreenarg;

		#endregion

		#region ================= Static properties

		public static string SupportedGames => supportedgames;
		public static GameHandler Current => current;

		#endregion

		#region ================= Delegates

		// Map title retrieval
		public delegate MapItem GetMapInfoDelegate(string mapname, BinaryReader reader, ResourceType restype);

		// Maps gathering
		protected delegate void GetFolderMapsDelegate(string modpath, Dictionary<string, MapItem> mapslist, GetMapInfoDelegate getmapinfo);
		protected delegate void    GetPakMapsDelegate(string modpath, Dictionary<string, MapItem> mapslist, GetMapInfoDelegate getmapinfo);
		protected delegate void    GetPK3MapsDelegate(string modpath, Dictionary<string, MapItem> mapslist, GetMapInfoDelegate getmapinfo);

		// Maps checking
		protected delegate bool FolderContainsMapsDelegate(string modpath);
		protected delegate bool PakContainsMapsDelegate(string modpath);
		protected delegate bool PK3ContainsMapsDelegate(string modpath);

		// File checking
		protected delegate bool PakContainsFileDelegate(string modpath, string filename);
		protected delegate bool PK3ContainsFileDelegate(string modpath, string filename);

		// Demo info retrieval
		protected delegate DemoItem GetDemoInfoDelegate(string demoname, BinaryReader reader, ResourceType restype);

		// Demos gathering
		protected delegate List<DemoItem> GetFolderDemosDelegate(string modpath, string demosfolder);
		protected delegate List<DemoItem>    GetPakDemosDelegate(string modpath, string demosfolder);
		protected delegate List<DemoItem>    GetPK3DemosDelegate(string modpath, string demosfolder);

		// Map title retrieval instance
		protected GetMapInfoDelegate getmapinfo;

		// Maps gathering instances
		protected GetFolderMapsDelegate getfoldermaps;
		protected GetPakMapsDelegate getpakmaps;
		protected GetPK3MapsDelegate getpk3maps;

		// Maps checking instances
		protected FolderContainsMapsDelegate foldercontainsmaps;
		protected PakContainsMapsDelegate pakscontainmaps;
		protected PK3ContainsMapsDelegate pk3scontainmaps;

		// File checking instances
		protected PakContainsFileDelegate pakscontainfile;
		protected PK3ContainsFileDelegate pk3scontainfile;

		// Demo info retrieval instance
		protected GetDemoInfoDelegate getdemoinfo;

		// Demo gathering instances
		protected GetFolderDemosDelegate getfolderdemos;
		protected GetPakDemosDelegate getpakdemos;
		protected GetPK3DemosDelegate getpk3demos;

		#endregion

		#region ================= Constructor / Setup

		protected GameHandler()
		{
			launchparams = new Dictionary<ItemType, string>();
			basegames = new Dictionary<string, GameItem>(StringComparer.OrdinalIgnoreCase);
			mapnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			defaultmapnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			skills = new List<SkillItem>();
			classes = new List<ClassItem>();
			skillnames = new List<string>();
			classnames = new List<string>();
			supporteddemoextensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			fullscreenarg = new Dictionary<bool, string>();
		}

		protected abstract bool CanHandle(string gamepath);

		protected virtual void Setup(string gamepath) // c:\games\Quake
		{
			this.gamepath = gamepath;

			// Add random skill and class
			if(skills.Count > 1) skills.Insert(0, SkillItem.Random);
			if(skills.Count > 0) skills.Insert(0, SkillItem.Default);

			if(classes.Count > 1) classes.Insert(0, ClassItem.Random);
			if(classes.Count > 0) classes.Insert(0, ClassItem.Default);

			// Store base game maps...
			defaultmaplist = new Dictionary<string, MapItem>(StringComparer.OrdinalIgnoreCase);
			var path = Path.Combine(gamepath, defaultmodpath);

			if (Directory.Exists(path))
			{
				getfoldermaps?.Invoke(path, defaultmaplist, getmapinfo);
				getpakmaps?.Invoke(path, defaultmaplist, getmapinfo);
				getpk3maps?.Invoke(path, defaultmaplist, getmapinfo);
			}
		}

		#endregion

		#region ================= Data gathering

		// Quakespasm can play demos made for both ID1 maps and currently selected official MP from any folder...
		public void UpdateDefaultMapNames(string modpath)
		{
			// Store maps from default mod...
			var maplist = new Dictionary<string, MapItem>(StringComparer.OrdinalIgnoreCase);

			// Get maps from all supported sources
			if(!string.IsNullOrEmpty(modpath) && Directory.Exists(modpath))
			{
				getfoldermaps?.Invoke(modpath, maplist, null);
				getpakmaps?.Invoke(modpath, maplist, null);
				getpk3maps?.Invoke(modpath, maplist, null);
			}

			// Store map names...
			defaultmapnames = new HashSet<string>(maplist.Keys, StringComparer.OrdinalIgnoreCase);

			// Add map names from base game...
			defaultmapnames.UnionWith(defaultmaplist.Keys);
		}

		// Because some engines have hardcoded resolution lists...
		public virtual List<ResolutionItem> GetVideoModes()
		{
			return DisplayTools.GetVideoModes();
		}

		public abstract List<ModItem> GetMods();

		public virtual List<DemoItem> GetDemos(string modpath) // c:\Quake\MyMod
		{
			return GetDemos(modpath, string.Empty);
		}

		protected virtual List<DemoItem> GetDemos(string modpath, string modfolder)
		{
			var demos = new List<DemoItem>();
			if(!Directory.Exists(modpath)) return demos;

			// Get demos from all supported sources
			var nameshash = new HashSet<string>();
			if(getfolderdemos != null) AddDemos(demos, getfolderdemos(modpath, modfolder), nameshash);
			if(getpakdemos != null) AddDemos(demos, getpakdemos(modpath, modfolder), nameshash);
			if(getpk3demos != null) AddDemos(demos, getpk3demos(modpath, modfolder), nameshash);

			// Sort and return the List
			demos.Sort((s1, s2) =>
			{
				if(s1.ResourceType != s2.ResourceType) return (int)s1.ResourceType > (int)s2.ResourceType ? 1 : -1; // Sort by ResourceType
				return s1.Value.CompareNatural(s2.Value);
			});
			return demos;
		}

		protected virtual void AddDemos(List<DemoItem> demos, List<DemoItem> newdemos, HashSet<string> nameshash)
		{
			foreach(DemoItem di in newdemos)
			{
				string hash = Path.GetFileName(di.MapFilePath) + di.Title;
				if(!nameshash.Contains(hash))
				{
					nameshash.Add(hash);
					demos.Add(di);
				}
			}
		}

		public virtual List<MapItem> GetMaps(string modpath) // c:\Quake\MyMod
		{
			modname = modpath.Substring(gamepath.Length + 1);
			if(!Directory.Exists(modpath)) return new List<MapItem>();
			var maplist = new Dictionary<string, MapItem>(StringComparer.OrdinalIgnoreCase);

			// Get maps from all supported sources
			getfoldermaps?.Invoke(modpath, maplist, getmapinfo);
			getpakmaps?.Invoke(modpath, maplist, getmapinfo);
			getpk3maps?.Invoke(modpath, maplist, getmapinfo);

			// Store map names...
			mapnames = new HashSet<string>(maplist.Keys, StringComparer.OrdinalIgnoreCase);
			mapnames.UnionWith(defaultmapnames); // Add default maps...

			// When mod has no maps, use maps from base game...
			if (maplist.Count == 0)
				maplist = defaultmaplist;

			// Sort and return the List
			var mapitems = new List<MapItem>(maplist.Values.Count);
			foreach(MapItem mi in maplist.Values) mapitems.Add(mi);
			mapitems.Sort((s1, s2) =>
			{
				if(s1.ResourceType != s2.ResourceType) return (int)s1.ResourceType > (int)s2.ResourceType ? 1 : -1; // Sort by ResourceType
				return s1.Value.CompareNatural(s2.Value);
			});
			return mapitems;
		} 

		public virtual List<EngineItem> GetEngines()
		{
			string[] enginenames = Directory.GetFiles(gamepath, "*.exe");
			var result = new List<EngineItem>();
			foreach(string engine in enginenames)
			{
				if(!IsEngine(Path.GetFileName(engine))) continue;

				ImageSource img = null;
				using(var i = Icon.ExtractAssociatedIcon(engine))
				{
					if(i != null)
						img = Imaging.CreateBitmapSourceFromHIcon(i.Handle, new Int32Rect(0, 0, i.Width, i.Height), BitmapSizeOptions.FromEmptyOptions());
				}

				result.Add(new EngineItem(img, engine));
			}

			return result;
		}

		public virtual string GetRandomItem(ItemType type)
		{
			switch(type)
			{
				case ItemType.CLASS: return (classnames.Count > 0 ? classnames[App.Random.Next(0, classnames.Count)] : "0");
				case ItemType.SKILL: return (skillnames.Count > 0 ? skillnames[App.Random.Next(0, skillnames.Count)] : "0");
				case ItemType.MAP: return mapnames.ElementAt(App.Random.Next(0, mapnames.Count));
				default: throw new Exception("GetRandomItem: unsupported ItemType!");
			}
		}

		// Here mainly because of Hexen 2...
		public virtual string CheckMapTitle(string title)
		{
			return title.Trim();
		}

		#endregion

		#region ================= Utility methods

		// "maps/mymap4.bsp", <mymap1, mymap2, mymap3...>
		public virtual bool EntryIsMap(string path, Dictionary<string, MapItem> mapslist)
		{
			path = path.ToLowerInvariant();
			if(Path.GetDirectoryName(path).EndsWith("maps") && Path.GetExtension(path) == ".bsp")
			{
				string mapname = Path.GetFileNameWithoutExtension(path);
				if((string.IsNullOrEmpty(ignoredmapprefix) || !mapname.StartsWith(ignoredmapprefix)) && !mapslist.ContainsKey(mapname))
					return true;
			}

			return false;
		}

		public virtual void AddDemoItem(string relativedemopath, List<DemoItem> demos, BinaryReader reader, ResourceType restype)
		{
			relativedemopath = relativedemopath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			DemoItem di = getdemoinfo(relativedemopath, reader, restype);
			if(di != null)
			{
				if(di.IsInvalid)
					demos.Add(di);
				else if(!mapnames.Contains(Path.GetFileNameWithoutExtension(di.MapFilePath))) // Check if we have a matching map...
					demos.Add(new DemoItem(relativedemopath, "Missing map file: '" + di.MapFilePath + "'", di.ResourceType)); // Add anyway, but with a warning...
				else if(!string.IsNullOrEmpty(di.ModName) && string.Compare(modname, di.ModName, StringComparison.OrdinalIgnoreCase) != 0)
					demos.Add(new DemoItem(relativedemopath, "Incorrect location: expected to be in '" + di.ModName + "' folder", di.ResourceType)); // Add anyway, but with a warning...
				else
					demos.Add(di);
			}
			else
			{
				// Add anyway, I guess...
				demos.Add(new DemoItem(relativedemopath, "Unknown demo format", restype));
			}
		}

		protected virtual bool IsEngine(string filename)
		{
			return (filename.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
				&& Path.GetFileNameWithoutExtension(filename) != App.AppName 
				&& !filename.StartsWith("unins", StringComparison.OrdinalIgnoreCase) 
				&& !filename.StartsWith("unwise", StringComparison.OrdinalIgnoreCase));
		}

		#endregion

		#region ================= Instancing

		public static bool Create(string gamepath) // c:\games\Quake
		{
			// Try to get appropriate game handler
			List<string> gametitles = new List<string>();
			foreach(Type type in Assembly.GetAssembly(typeof(GameHandler)).GetTypes()
				.Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(GameHandler))))
			{
				var gh = (GameHandler)Activator.CreateInstance(type);
				gametitles.Add(gh.GameTitle);
				if(current == null && gh.CanHandle(gamepath))
				{
					current = gh;
					gh.Setup(gamepath); // GameItems created in Setup() reference GameHandler.Current...
				}
			}

			// Store all titles
			supportedgames = string.Join(" / ", gametitles.ToArray());

			return current != null;
		}

		#endregion
	}
}
