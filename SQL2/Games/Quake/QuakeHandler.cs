﻿#region ================= Namespaces

using System.Collections.Generic;
using System.IO;
using mxd.SQL2.DataReaders;
using mxd.SQL2.Items;

#endregion

namespace mxd.SQL2.Games.Quake
{
	public class QuakeHandler : GameHandler
	{
		#region ================= Properties

		public override string GameTitle => "Quake";

		#endregion

		#region ================= Setup

		// Valid Quake path if "id1\pak0.pak" exists, I guess...
		protected override bool CanHandle(string gamepath)
		{
			return File.Exists(Path.Combine(gamepath, "id1\\pak0.pak"));
		}

		// Data initialization order matters (horrible, I know...)!
		protected override void Setup(string gamepath)
		{
			// Default mod path
			defaultmodpath = Path.Combine(gamepath, "ID1").ToLowerInvariant();

			// Ignore props
			ignoredmapprefix = "b_";

			// Demo extensions
			supporteddemoextensions.Add(".dem");
			supporteddemoextensions.Add(".mvd"); // net Quake only?
			supporteddemoextensions.Add(".qwd"); // net Quake only?

			// Setup map delegates
			getfoldermaps = DirectoryReader.GetMaps;
			getpakmaps = PAKReader.GetMaps;
			getpk3maps = PK3Reader.GetMaps;

			foldercontainsmaps = DirectoryReader.ContainsMaps;
			pakscontainmaps = PAKReader.ContainsMaps;
			pk3scontainmaps = PK3Reader.ContainsMaps;

			getmapinfo = QuakeBSPReader.GetMapInfo;

			// Setup demo delegates
			getfolderdemos = DirectoryReader.GetDemos;
			getpakdemos = PAKReader.GetDemos;
			getpk3demos = PK3Reader.GetDemos;

			getdemoinfo = QuakeDemoReader.GetDemoInfo;

			// Setup file checking delegates
			pakscontainfile = PAKReader.ContainsFile;
			pk3scontainfile = PK3Reader.ContainsFile;

			// Setup fullscreen args...
			fullscreenarg[true] = string.Empty;
			fullscreenarg[false] = "-window ";

			// Setup launch params
			launchparams[ItemType.ENGINE] = string.Empty;
			launchparams[ItemType.RESOLUTION] = "{2}-width {0} -height {1}";
			launchparams[ItemType.GAME] = string.Empty;
			launchparams[ItemType.MOD] = "-game {0}";
			launchparams[ItemType.MAP] = "+map {0}";
			launchparams[ItemType.SKILL] = "+skill {0}";
			launchparams[ItemType.CLASS] = string.Empty;
			launchparams[ItemType.DEMO] = "+playdemo {0}";

			// Setup skills (requires launchparams)
			skills.AddRange(new[]
			{
				new SkillItem("Easy", "0"),
				new SkillItem("Normal", "1", true),
				new SkillItem("Hard", "2"),
				new SkillItem("Nightmare!", "3", false, true)
			});

			// Setup basegames (requires defaultmodpath)
			basegames["ID1"] = new GameItem("Quake", "id1", "");
			basegames["QUOTH"] = new GameItem("Quoth", "quoth", "-quoth");
			basegames["NEHAHRA"] = new GameItem("Nehahra", "nehahra", "-nehahra");
			basegames["HIPNOTIC"] = new GameItem("MP1: Scourge of Armagon", "hipnotic", "-hipnotic");
			basegames["ROGUE"] = new GameItem("MP2: Dissolution of Eternity", "rogue", "-rogue");

			// Pass on to base...
			base.Setup(gamepath);
		}

		#endregion

		#region ================= Methods

		public override List<ModItem> GetMods()
		{
			var result = new List<ModItem>();
			GetMods(gamepath, result);
			return result;
		}

		private void GetMods(string path, ICollection<ModItem> result)
		{
			foreach(string folder in Directory.GetDirectories(path))
			{
				if(!Directory.Exists(folder)) continue;
				
				string name = folder.Substring(gamepath.Length + 1);
				if(basegames.ContainsKey(name))
				{
					result.Add(new ModItem(name, folder, true));
					continue;
				}

				// Count folder as a mod when it contains "progs.dat"...
				if(File.Exists(Path.Combine(folder, "progs.dat")) || pakscontainfile(folder, "progs.dat") || pk3scontainfile(folder, "progs.dat"))
				{
					result.Add(new ModItem(name, folder));
					continue;
				}

				// If current folder has no maps, try subfolders...
				if(!foldercontainsmaps(folder) && !pakscontainmaps(folder) && !pk3scontainmaps(folder))
				{
					GetMods(folder, result);
					continue;
				}

				result.Add(new ModItem(name, folder));
			}
		}

		#endregion
	}
}
