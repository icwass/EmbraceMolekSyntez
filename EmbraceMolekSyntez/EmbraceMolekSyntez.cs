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
	private static IDetour hook_Sim_method_1829;
	public static PartType EmitterSimple;
	public static Texture debug_arrow, emitterIcon, emitterIconHover, emitter_armBase;

	const int NoTranslate = 0;
	const int GrabDropTranslate = 1;
	//const int MovementTranslate = 2;


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

	//---------------------------------------------------//
	//internal helper methods
	private static void BeamHelper(Part emitter, PartSimState partSimState, List<Molecule> molecules)
	{
		//prepare to find where the gripper should go
		HexIndex pos = partSimState.field_2724;
		int rotation = (partSimState.field_2726.GetNumberOfTurns() % 6 + 6) % 6;
		Func<HexIndex, bool> inSight; // true if the hex is inSight of the beam
		Func<HexIndex, int> beamDistance; // if inSight, distance between arm base and target
		switch (rotation)
		{
			default:
			case 0: inSight = hex => hex.Q > pos.Q &&		  hex.R == pos.R		;	beamDistance = hex => hex.Q - pos.Q;	break;
			case 1: inSight = hex => hex.R > pos.R &&		  hex.Q == pos.Q		;	beamDistance = hex => hex.R - pos.R;	break;
			case 2: inSight = hex => hex.R > pos.R && hex.Q + hex.R == pos.Q + pos.R;	beamDistance = hex => hex.R - pos.R;	break;
			case 3: inSight = hex => hex.Q < pos.Q &&		  hex.R == pos.R		;	beamDistance = hex => pos.Q - hex.Q;	break;
			case 4: inSight = hex => hex.R < pos.R &&		  hex.Q == pos.Q		;	beamDistance = hex => pos.R - hex.R;	break;
			case 5: inSight = hex => hex.R < pos.R && hex.Q + hex.R == pos.Q + pos.R;	beamDistance = hex => pos.R - hex.R;	break;
		}
		//move the gripper to the nearest atom it's looking at
		//if there is none in that direction, or we hit a chamber wall first, place it on the arm base instead (should be guaranteed no atom)
		int min = int.MaxValue;
		HexIndex minHex = pos;
		foreach (var molecule in molecules)
		{
			foreach (var hex in molecule.method_1100().Keys)
			{
				if (inSight(hex) && beamDistance(hex) < min)
				{
					min = beamDistance(hex);
					minHex = hex;
				}
			}
		}
		List<HexIndex> chamberHexes = new(); ///////////////////////////////////////////////////////////////////// replace later with actual list of chamber wall hexes
		foreach (var hex in chamberHexes)
		{
			if (inSight(hex) && beamDistance(hex) < min)
			{
				minHex = pos;
				break;
			}
		}
		// change arm length so the gripper sits in the desired position
		partSimState.field_2725 = beamDistance(minHex);
		partSimState.field_2737 = 0;
	}




	//---------------------------------------------------//


	public override void Load()
	{
		//
	}

	public override void LoadPuzzleContent()
	{
		string path = "embraceMolekSyntez/textures/parts/";
		debug_arrow = class_235.method_615(path + "debug_arrow");
		emitterIcon = class_235.method_615(path + "icons/emitter");
		emitterIconHover = class_235.method_615(path + "icons/emitter_hover");
		emitter_armBase = class_235.method_615(path + "emitter/arm_base");

		//more advanced emitters can trash and output targets, or they can rotate their base
		//Swivel-Mount Manipulator: 50g, can also rotate to face in a different direction
		//Interfacing Manipulator: 70g, can also trash and output targets
		//Universal Manipulator: 100g, a swivel-mount and interfacing manipulator

		EmitterSimple = new PartType()
			{
				/*ID*/field_1528 = "embrace-moleksyntez-emitter",
				/*Name*/field_1529 = class_134.method_253("Fixed-Direction Manipulator", string.Empty),
				/*Desc*/field_1530 = class_134.method_253("A standard manipulator that can push, pull, and pivot targeted alchemicules. Push and pull are executed using the Extend and Retract instructions, respectively.", string.Empty),
				/*Cost*/field_1531 = 40,
				/*Type*/field_1532 = (enum_2) 1,//default=(enum_2)0; arm=(enum_2)1; track=(enum_2)2;
				/*Programmable?*/field_1533 = true,//default=false, which disables programmability and atom collision
				/*Gripper Positions*/field_1534 = new HexRotation[1] { HexRotation.R0 },//default=empty; each entry defines a gripper
				/*Piston?*/field_1535 = true,//default=false
				/*Force-rotatable*/field_1536 = true,//default=false, but true for arms and the berlo, which are 1-hex big but can be rotated individually
				/*Hex Footprint*/field_1540 = new HexIndex[1] { new HexIndex(0, 0) },//default=emptyList
				/*Icon*/field_1547 = emitterIcon,
				/*Hover Icon*/field_1548 = emitterIconHover,
				/*Glow (Shadow)*/ field_1549 = class_238.field_1989.field_97.field_382,
				/*Stroke (Outline)*/ field_1550 = class_238.field_1989.field_97.field_383,
				/*Permissions*/field_1551 = Permissions.SimpleArm,
			};


		QApi.AddPartType(EmitterSimple, (part, pos, editor, renderer) => {
			//draw code
			renderer.method_528(emitter_armBase, new HexIndex(0,0), new Vector2(0.0f, 0.0f));

			Vector2 vector2_24 = new Vector2(41f, 48f);
			renderer.method_521(debug_arrow, vector2_24 + new Vector2(-9f, -21f));
		});

		QApi.AddPartTypeToPanel(EmitterSimple, PartTypes.field_1768);//inserts part type after piston


		FakeGripper.LoadPuzzleContent();
		//------------------------- HOOKING -------------------------//
		On.CompiledProgramGrid.method_852 += CompiledProgramGrid_Method_852; // interfere with how instructions are read
		hook_Sim_method_1829 = new Hook(
			typeof(Sim).GetMethod("method_1829", BindingFlags.Instance | BindingFlags.NonPublic),
			typeof(MainClass).GetMethod("OnSimMethod1829", BindingFlags.Static | BindingFlags.NonPublic)
		);
	}

	public override void Unload()
	{
		FakeGripper.Unload();
		hook_Sim_method_1829.Dispose();
	}

	private delegate void orig_Sim_method_1829(Sim self, enum_127 param_5366);

	private static void OnSimMethod1829(orig_Sim_method_1829 orig, Sim sim_self, enum_127 param_5366)
	{
		var sim_dyn = new DynamicData(sim_self);
		var partSimStates = sim_dyn.Get<Dictionary<Part, PartSimState>>("field_3821");
		var SEB = sim_dyn.Get<SolutionEditorBase>("field_3818");
		var solution = SEB.method_502();
		var partList = solution.field_3919;
		var emitterList = new List<Part>(partList.Where(x => x.method_1159() == EmitterSimple));
		var emitterDict = new Dictionary<Part, int>();
		var molecules = sim_dyn.Get<List<Molecule>>("field_3823");

		bool isCycleZero = sim_self.method_1818() == 0;

		
		
		foreach (var emitter in emitterList)
		{
			//
			if (isCycleZero) // remove the extend/retract limits
			{
				//FieldInfo fieldInfoMin = typeof(Part).GetField("field_2689", BindingFlags.Static | BindingFlags.Public);
				//fieldInfoMin.SetValue(null, 0);
				//FieldInfo fieldInfoMax = typeof(Part).GetField("field_2690", BindingFlags.Static | BindingFlags.Public);
				//fieldInfoMax.SetValue(null, int.MaxValue);
			}

			//save the index value
			emitterDict.Add(emitter, emitter.field_2703);
			//overwrite so we can read it in method_852 as a parameter
			if (param_5366 == (enum_127)1)
			{
				emitter.field_2703 = GrabDropTranslate;
			}
			else
			{
				emitter.field_2703 = NoTranslate;
			}
			BeamHelper(emitter, partSimStates[emitter], molecules);
		}

		sim_dyn.Set("field_3821", partSimStates);
		///////////////
		orig(sim_self, param_5366);
		///////////////
		partSimStates = sim_dyn.Get<Dictionary<Part, PartSimState>>("field_3821");
		molecules = sim_dyn.Get<List<Molecule>>("field_3823");

		//run my own version of method_1829 as needed:
		//- all interfacing emitters with an output instruction DROP, then attempt to output their molecule
		//- all interfacing emitters with a trash instruction DROP, then trash their molecule
		//- all non-interfacing emitters with an inferfacing instruction CRASH
		//- all non-swivel-mount emitters with a swivel instruction CRASH




		//param_5366 == (enum_127)1 => normal arms run grab/close instructions
		//param_5366 == (enum_127)0 => normal arms run every other type of instruction


		//instructionType.field_2548 == (enum_144) 1, field_2549 == true  =>   // extend
		//instructionType.field_2548 == (enum_144) 1, field_2549 == false =>   // retract
		//instructionType.field_2548 == (enum_144) 2, field_2549 == true  =>   // rotate clockwise
		//instructionType.field_2548 == (enum_144) 2, field_2549 == false =>   // rotate counterclockwise
		//instructionType.field_2548 == (enum_144) 3, field_2549 == true  =>   // pivot clockwise
		//instructionType.field_2548 == (enum_144) 3, field_2549 == false =>   // pivot counterclockwise
		//instructionType.field_2548 == (enum_144) 4, field_2549 == true  =>   // track [+]
		//instructionType.field_2548 == (enum_144) 4, field_2549 == false =>   // track [-]
		//instructionType.field_2548 == (enum_144) 5, field_2549 == true  =>   // grab
		//instructionType.field_2548 == (enum_144) 5, field_2549 == false =>   // close

		//universal emitter behavior:
		//
		//extend					drop any molecule i'm holding, grab molecule i'm looking at, push away from me
		//retract					drop any molecule i'm holding, grab molecule i'm looking at, pull towards me
		//rotate clockwise			drop any molecule i'm holding, rotate clockwise
		//rotate counterclockwise	drop any molecule i'm holding, rotate counterclockwise
		//pivot clockwise			drop any molecule i'm holding, grab molecule i'm looking at, pivot clockwise
		//pivot counterclockwise	drop any molecule i'm holding, grab molecule i'm looking at, pivot counterclockwise
		//track [+]					drop any molecule i'm holding, move [+] on track
		//track [-]					drop any molecule i'm holding, move [-] on track
		//grab						attempt to output molecule, highest priority, overrides trash and movement
		//close						trash molecule

		//restore the index values
		foreach (var kvp in emitterDict)
		{
			var emitter = kvp.Key;
			emitter.field_2703 = emitterDict[emitter];
			BeamHelper(emitter, partSimStates[emitter], molecules);
		}
		sim_dyn.Set("field_3821", partSimStates);
	}

	//------------------------- END HOOKING -------------------------//

	public static InstructionType CompiledProgramGrid_Method_852(
		On.CompiledProgramGrid.orig_method_852 orig,
		CompiledProgramGrid cpg_self,
		int param_4507,
		Part part,
		out Maybe<int> param_4509
		)
	{
		Maybe<int> tempOut;
		InstructionType ret = orig(cpg_self, param_4507, part, out tempOut);
		param_4509 = tempOut;

		var partType = part.method_1159();
		enum_144 instr = ret.field_2548;
		bool partTypeFlag = partType == EmitterSimple || partType == EmitterSimple;

		//translate the instruction, if needed
		if (partTypeFlag && part.field_2703 == GrabDropTranslate)
		{
			//translate the emitter instruction for the grab/drop-instruction phase
			switch (instr)
			{
				case (enum_144)0: // NOOP
				case (enum_144)2: // ROTATE
				case (enum_144)4: // TRACK
					ret = class_169.field_1664; // DROP
					break;
				case (enum_144)1: // PISTON
				case (enum_144)3: // PIVOT
				case (enum_144)5: // GRAB/DROP
					ret = class_169.field_1663; // GRAB
					break;
				default:
					break;
			}
		}
		//else if (partTypeFlag && part.field_2703 == MovementTranslate)
		//{
		//	//assuming the translated instruction in the grab-drop-instruction phase was executed correctly,
		//	//then there's no need to translate the instruction during the movement-instruction phase,
		//	//the literal instruction will give the desired behavior
		//}
		else
		{
			//either it's NOT an emitter, or we want the literal instruction
			//so, don't translate
		}
		//return the translated instruction
		return ret;
	}

	public override void PostLoad()
	{
		//
	}
}