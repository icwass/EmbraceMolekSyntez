using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using Quintessential;
using Quintessential.Settings;
using SDL2;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace EmbraceMolekSyntez;

using PartType = class_139;
using Permissions = enum_149;
//using BondType = enum_126;
//using BondSite = class_222;
//using AtomTypes = class_175;
using PartTypes = class_191;
using Texture = class_256;

public class MainClass : QuintessentialMod
{
	public static Texture debug_arrow;

	public static void playSound(Sound SOUND, float VOLUME, Sim sim = null, SolutionEditorBase seb = null)
	{
		float FACTOR = 1f;
		if (sim != null)
		{
			seb = new DynamicData(sim).Get<SolutionEditorBase>("field_3818");
		}
		if (seb != null)
		{
			if (seb is class_194) // GIF recording, so mute
			{
				FACTOR = 0.0f;
			}
			else if (seb is SolutionEditorScreen)
			{
				var seb_dyn = new DynamicData(seb);
				bool isQuickMode = seb_dyn.Get<Maybe<int>>("field_4030").method_1085();
				FACTOR = isQuickMode ? 0.5f : 1f;
			}
		}
		class_158.method_376(SOUND.field_4061, class_269.field_2109 * VOLUME * FACTOR, false);
	}

	public override void Load()
	{
		//
	}

	public override void LoadPuzzleContent()
	{
		debug_arrow = class_235.method_615("embraceInfinifactory/textures/parts/debug_arrow");

		FakeGripper.LoadPuzzleContent();
	}

	public override void Unload()
	{
		FakeGripper.Unload();
	}

	public override void PostLoad()
	{
		//
	}
}