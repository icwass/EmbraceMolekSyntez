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
	private static IDetour hook_SEB_method_2012;
	private static PartType EmitterSimple, EmitterSwivel, EmitterIO, EmitterUniversal;
	public static Texture debug_arrow, debug_arrowIO, emitterIcon, emitterIconHover, emitter_armBase, emitter_armBaseSwivel;

	private static List<PartType> emitterPartTypes;

	private const int NoDecoding = 0;
	private const int GrabDropDecoding = 1;
	private const int MovementDecoding = 2;

	private static LocString error_trash;
	private static LocString error_output;
	private static LocString error_rotate;

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
	public static HexIndex getHexFromPoint(Vector2 point, Vector2 reference)
	{
		return class_187.field_1742.method_493(point, reference);
	}


	private static HexIndex BeamHelper(PartSimState partSimState, List<Molecule> molecules, out int dist)
	{
		//prepare to find where the gripper should go
		HexIndex pos = partSimState.field_2724;
		int rotation = (partSimState.field_2726.GetNumberOfTurns() % 6 + 6) % 6;
		Func<HexIndex, bool> inSight; // true if the hex is inSight of the beam
		Func<HexIndex, int> beamDistance; // if inSight, distance between arm base and target
		switch (rotation)
		{
			default:
			case 0: inSight = hex =>		 hex.R == pos.R			&& hex.Q > pos.Q;	beamDistance = hex => hex.Q - pos.Q;	break;
			case 1: inSight = hex =>		 hex.Q == pos.Q			&& hex.R > pos.R;	beamDistance = hex => hex.R - pos.R;	break;
			case 2: inSight = hex => hex.Q + hex.R == pos.Q + pos.R	&& hex.R > pos.R;	beamDistance = hex => hex.R - pos.R;	break;
			case 3: inSight = hex => 		 hex.R == pos.R			&& hex.Q < pos.Q;	beamDistance = hex => pos.Q - hex.Q;	break;
			case 4: inSight = hex => 		 hex.Q == pos.Q			&& hex.R < pos.R;	beamDistance = hex => pos.R - hex.R;	break;
			case 5: inSight = hex => hex.Q + hex.R == pos.Q + pos.R	&& hex.R < pos.R;	beamDistance = hex => pos.R - hex.R;	break;
		}
		//look for the nearest inSight atom
		//if there is none in that direction, or we hit a chamber wall first, look at the armbase instead
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
		List<HexIndex> chamberHexes = new(); //////////////////////////////////// replace later with actual list of chamber wall hexes, somehow
		foreach (var hex in chamberHexes)
		{
			if (inSight(hex) && beamDistance(hex) < min)
			{
				minHex = pos;
				min = 0;
				break;
			}
		}

		if (min == int.MaxValue || minHex == pos)
		{
			min = 0;
		}

		dist = min;
		return minHex;
	}

	//---------------------------------------------------//

	public override void Load()
	{
		//
	}

	public override void LoadPuzzleContent()
	{
		error_trash = class_134.method_253("This arm cannot dispose of alchemicules.", string.Empty);
		error_output = class_134.method_253("This arm cannot output alchemicules.", string.Empty);
		error_rotate = class_134.method_253("This arm cannot rotate.", string.Empty);
		
		
		string path = "embraceMolekSyntez/textures/parts/";
		debug_arrow = class_235.method_615(path + "debug_arrow");
		debug_arrowIO = class_235.method_615(path + "debug_arrow_io");
		emitterIcon = class_235.method_615(path + "icons/emitter");
		emitterIconHover = class_235.method_615(path + "icons/emitter_hover");
		emitter_armBase = class_235.method_615(path + "emitter/arm_base");
		emitter_armBaseSwivel = class_235.method_615(path + "emitter/arm_base_swivel");

		//more advanced emitters can trash and output targets, or they can rotate their base
		//Swivel-Mount Manipulator: 50g, can also rotate to face in a different direction
		//Interfacing Manipulator: 70g, can also trash and output targets
		//Universal Manipulator: 100g, a swivel-mount and interfacing manipulator

		float gx = -12f;
		float gy = -18.5f;


		EmitterSimple = new PartType()
		{
			/*ID*/field_1528 = "embrace-moleksyntez-emitter",
			/*Name*/field_1529 = class_134.method_253("Fixed-Direction Manipulator", string.Empty),
			/*Desc*/field_1530 = class_134.method_253("A standard manipulator that can push, pull, and pivot targeted alchemicules. Push and pull are executed using the Extend and Retract instructions, respectively.", string.Empty),
			/*Cost*/field_1531 = 20,
			/*Type*/field_1532 = (enum_2) 1,//default=(enum_2)0; arm=(enum_2)1; track=(enum_2)2;
			/*Programmable?*/field_1533 = true,//default=false, which disables programmability and atom collision
			/*Gripper Positions*/field_1534 = new HexRotation[1] { HexRotation.R0 },//default=empty; each entry defines a gripper
			/*Piston?*/field_1535 = true,//default=false
			/*Force-rotatable*/field_1536 = true,//default=false, but true for arms and the berlo, which are 1-hex big but can be rotated individually
			/*Icon*/field_1547 = emitterIcon,
			/*Hover Icon*/field_1548 = emitterIconHover,
			/*Permissions*/field_1551 = Permissions.SimpleArm,
		};

		QApi.AddPartType(EmitterSimple, (part, pos, editor, renderer) => {
			//draw code
			renderer.method_528(emitter_armBase, new HexIndex(0, 0), new Vector2(0.0f, 0.0f));

			Vector2 vector2_24 = new Vector2(41f, 48f);
			renderer.method_521(debug_arrow, vector2_24 + new Vector2(gx, gy));
		});

		EmitterSwivel = new PartType()
		{
			/*ID*/field_1528 = "embrace-moleksyntez-emitter-swivel",
			/*Name*/field_1529 = class_134.method_253("Rotating Manipulator", string.Empty),
			/*Desc*/field_1530 = class_134.method_253("A manipulator that can push, pull, and pivot targeted alchemicules, as well as rotate to aim in a different direction.", string.Empty),
			/*Cost*/field_1531 = 30,
			/*Type*/field_1532 = (enum_2) 1,//default=(enum_2)0; arm=(enum_2)1; track=(enum_2)2;
			/*Programmable?*/field_1533 = true,//default=false, which disables programmability and atom collision
			/*Gripper Positions*/field_1534 = new HexRotation[1] { HexRotation.R0 },//default=empty; each entry defines a gripper
			/*Piston?*/field_1535 = true,//default=false
			/*Force-rotatable*/field_1536 = true,//default=false, but true for arms and the berlo, which are 1-hex big but can be rotated individually
			/*Icon*/field_1547 = emitterIcon,
			/*Hover Icon*/field_1548 = emitterIconHover,
			/*Permissions*/field_1551 = Permissions.SimpleArm,
		};

		QApi.AddPartType(EmitterSwivel, (part, pos, editor, renderer) => {
			//draw code
			renderer.method_528(emitter_armBaseSwivel, new HexIndex(0, 0), new Vector2(0.0f, 0.0f));

			Vector2 vector2_24 = new Vector2(41f, 48f);
			renderer.method_521(debug_arrow, vector2_24 + new Vector2(gx, gy));
		});

		EmitterIO = new PartType()
		{
			/*ID*/field_1528 = "embrace-moleksyntez-emitter-io",
			/*Name*/field_1529 = class_134.method_253("Interface Manipulator", string.Empty),
			/*Desc*/field_1530 = class_134.method_253("A manipulator that can push, pull, pivot, output, and dispose targeted alchemicules.", string.Empty),
			/*Cost*/field_1531 = 50,
			/*Type*/field_1532 = (enum_2) 1,//default=(enum_2)0; arm=(enum_2)1; track=(enum_2)2;
			/*Programmable?*/field_1533 = true,//default=false, which disables programmability and atom collision
			/*Gripper Positions*/field_1534 = new HexRotation[1] { HexRotation.R0 },//default=empty; each entry defines a gripper
			/*Piston?*/field_1535 = true,//default=false
			/*Force-rotatable*/field_1536 = true,//default=false, but true for arms and the berlo, which are 1-hex big but can be rotated individually
			/*Icon*/field_1547 = emitterIcon,
			/*Hover Icon*/field_1548 = emitterIconHover,
			/*Permissions*/field_1551 = Permissions.SimpleArm,
		};

		QApi.AddPartType(EmitterIO, (part, pos, editor, renderer) => {
			//draw code
			renderer.method_528(emitter_armBase, new HexIndex(0, 0), new Vector2(0.0f, 0.0f));

			Vector2 vector2_24 = new Vector2(41f, 48f);
			renderer.method_521(debug_arrowIO, vector2_24 + new Vector2(gx, gy));
		});

		EmitterUniversal = new PartType()
		{
			/*ID*/field_1528 = "embrace-moleksyntez-emitter-universal",
			/*Name*/field_1529 = class_134.method_253("Universal Manipulator", string.Empty),
			/*Desc*/field_1530 = class_134.method_253("A manipulator that can push, pull, pivot, output, and dispose targeted alchemicules, as well as rotate to aim in a different direction.", string.Empty),
			/*Cost*/field_1531 = 80,
			/*Type*/field_1532 = (enum_2) 1,//default=(enum_2)0; arm=(enum_2)1; track=(enum_2)2;
			/*Programmable?*/field_1533 = true,//default=false, which disables programmability and atom collision
			/*Gripper Positions*/field_1534 = new HexRotation[1] { HexRotation.R0 },//default=empty; each entry defines a gripper
			/*Piston?*/field_1535 = true,//default=false
			/*Force-rotatable*/field_1536 = true,//default=false, but true for arms and the berlo, which are 1-hex big but can be rotated individually
			/*Icon*/field_1547 = emitterIcon,
			/*Hover Icon*/field_1548 = emitterIconHover,
			/*Permissions*/field_1551 = Permissions.SimpleArm,
		};

		QApi.AddPartType(EmitterUniversal, (part, pos, editor, renderer) => {
			//draw code
			renderer.method_528(emitter_armBaseSwivel, new HexIndex(0, 0), new Vector2(0.0f, 0.0f));

			Vector2 vector2_24 = new Vector2(41f, 48f);
			renderer.method_521(debug_arrowIO, vector2_24 + new Vector2(gx, gy));
		});

		QApi.AddPartTypeToPanel(EmitterSimple, PartTypes.field_1768);//inserts part type after piston
		QApi.AddPartTypeToPanel(EmitterSwivel, PartTypes.field_1768);//inserts part type after piston
		//QApi.AddPartTypeToPanel(EmitterIO, PartTypes.field_1768);//inserts part type after piston
		//QApi.AddPartTypeToPanel(EmitterUniversal, PartTypes.field_1768);//inserts part type after piston

		emitterPartTypes = new() { EmitterSimple, EmitterSwivel, EmitterIO, EmitterUniversal };

		//FakeGripper.LoadPuzzleContent();
		//------------------------- HOOKING -------------------------//
		On.CompiledProgramGrid.method_852 += CompiledProgramGrid_Method_852; // interfere with how instructions are read
		hook_Sim_method_1829 = new Hook(
			typeof(Sim).GetMethod("method_1829", BindingFlags.Instance | BindingFlags.NonPublic),
			typeof(MainClass).GetMethod("OnSimMethod1829", BindingFlags.Static | BindingFlags.NonPublic)
		);
		hook_SEB_method_2012 = new Hook(
			typeof(SolutionEditorBase).GetMethod("method_2012", BindingFlags.Instance | BindingFlags.NonPublic),
			typeof(MainClass).GetMethod("OnSEBMethod2012", BindingFlags.Static | BindingFlags.NonPublic)
		);
	}

	public override void Unload()
	{
		//FakeGripper.Unload();
		hook_Sim_method_1829.Dispose();
		hook_SEB_method_2012.Dispose();
	}

	private delegate void orig_Sim_method_1829(Sim self, enum_127 param_5366);
	private delegate Maybe<Part> orig_SolutionEditorBase_method_2012(SolutionEditorBase self, Vector2 param_5606, Vector2 param_5607, IEnumerable<Part> param_5608);

	private static void OnSimMethod1829(orig_Sim_method_1829 orig, Sim sim_self, enum_127 param_5366)
	{
		bool isGrabDropPhase = param_5366 != 0;
		var sim_dyn = new DynamicData(sim_self);
		var SEB = sim_dyn.Get<SolutionEditorBase>("field_3818");
		var solution = SEB.method_502();
		var partList = solution.field_3919;
		var partSimStates = sim_dyn.Get<Dictionary<Part, PartSimState>>("field_3821");
		var class401s = sim_dyn.Get<Dictionary<Part, Sim.class_401>>("field_3822");
		var molecules = sim_dyn.Get<List<Molecule>>("field_3823");
		var droppedMolecules = sim_dyn.Get<List<Molecule>>("field_3828");

		var emitterList = new List<Part>(partList.Where(x => emitterPartTypes.Contains(x.method_1159())));
		var emitterDict = new Dictionary<Part, int>();

		// prep for method_1829
		foreach (var emitter in emitterList)
		{
			// update the emitter and its manipulator
			var emitterState = partSimStates[emitter];
			var manipulator = emitter.field_2696[0];
			var manipulatorState = partSimStates[manipulator];
			
			if (isGrabDropPhase) 
			{
				//then ungrip the manipulator
				Molecule molecule;
				if (manipulatorState.field_2729.method_99<Molecule>(out molecule) && !droppedMolecules.Contains(molecule))
				{
					//drop the molecule, too
					droppedMolecules.Add(molecule);
				}
				manipulatorState.field_2728 = false;
				manipulatorState.field_2729 = (Maybe<Molecule>) struct_18.field_1431;
				manipulatorState.field_2740 = false;

				//update emitter and manipulator to the new pos
				var oldPos = manipulatorState.field_2724;
				int dist;
				var newPos = BeamHelper(partSimStates[emitter], molecules, out dist);
				emitterState.field_2725 = dist;
				emitterState.field_2736 = dist;
				manipulatorState.field_2724 = newPos;
				manipulatorState.field_2734 = newPos;
			}

			//save the index value, just in case
			emitterDict.Add(emitter, emitter.field_2703);
			//overwrite so we can read it in method_852 as a parameter
			if (isGrabDropPhase)
			{
				emitter.field_2703 = GrabDropDecoding;
			}
			else
			{
				emitter.field_2703 = MovementDecoding;
			}
		}
		
		sim_dyn.Set("field_3821", partSimStates);
		sim_dyn.Set("field_3828", droppedMolecules);
		///////////////
		orig(sim_self, param_5366);
		///////////////
		partSimStates = sim_dyn.Get<Dictionary<Part, PartSimState>>("field_3821");

		//custom instruction-execution code
		var compiledProgramSim = sim_self.method_1820();
		int cycleNumber = sim_self.method_1818();
		MethodInfo Method_1852 = typeof(Sim).GetMethod("method_1852", BindingFlags.NonPublic | BindingFlags.Instance);
		foreach (var emitter in emitterList)
		{
			var emitterState = partSimStates[emitter];
			var manipulator = emitter.field_2696[0];
			var manipulatorState = partSimStates[manipulator];
			Vector2 errorPos1 = class_187.field_1742.method_492(emitterState.field_2724);
			Vector2 errorPos2 = class_187.field_1742.method_492(manipulatorState.field_2724);

			// don't animate the gripper grabbing/dropping
			manipulatorState.field_2740 = false;

			//overwrite the index again and fetch the ACTUAL instruction
			emitter.field_2703 = NoDecoding;
			var instructionType = compiledProgramSim.method_852(cycleNumber, emitter, out Maybe<int> _);
			var instructionCategory = instructionType.field_2548;

			//then execute
			if (isGrabDropPhase)
			{
				if (instructionCategory == (enum_144) 5) // GRAB/DROP
				{
					bool isGrab = instructionType.field_2549;
					if (emitter.method_1159() == EmitterSimple || emitter.method_1159() == EmitterSwivel)
					{
						SEB.method_518(0.0f, isGrab ? error_output : error_trash, new Vector2[2] { errorPos1, errorPos2 });
					}
					else
					{
						if (isGrab)
						{
							//try to output the molecule ///////////////////////////////////////////////////////////////////////////////////////
						}
						else
						{
							//trash the molecule ///////////////////////////////////////////////////////////////////////////////////////////////
						}
					}
				}
			}
			else // MOVEMENT PHASE
			{
				if (instructionCategory == (enum_144)2) // ROTATE
				{
					if (emitter.method_1159() == EmitterSimple || emitter.method_1159() == EmitterIO)
					{
						SEB.method_518(0.0f, error_rotate, new Vector2[1] { errorPos1 });
					}
					else
					{
						// rotating
					}
				}
				else if (instructionCategory == (enum_144)1) // PISTON
				{
					// extend/retract is the only movement we need to cover explicitly
					int q = instructionType.field_2549 ? 1 : -1;
					int num = emitterState.field_2725 + q;

					if (manipulatorState.field_2729.method_99<Molecule>(out _))
					{
						//holding a molecule i can actually move
						emitterState.field_2725 += q;
						emitterState.field_2737 = q;
						for (int index = 0; index < emitter.field_2696.Length; ++index)
						{
							HexRotation hexRotation;
							if (emitter.method_1159().method_311(index).method_99<HexRotation>(out hexRotation))
							{
								HexIndex hexIndex = new HexIndex(q, 0).Rotated(hexRotation + emitterState.field_2726);
								Method_1852.Invoke(sim_self, new object[] { emitter.field_2696[index], hexIndex });
							}
						}
					}
				}
			}
		}

		//restore the index values
		foreach (var kvp in emitterDict)
		{
			var emitter = kvp.Key;
			emitter.field_2703 = emitterDict[emitter];
		}
		sim_dyn.Set("field_3821", partSimStates);
	}

	private static Maybe<Part> OnSEBMethod2012(orig_SolutionEditorBase_method_2012 orig, SolutionEditorBase SEB_self, Vector2 param_5606, Vector2 param_5607, IEnumerable<Part> param_5608)
	{
		Maybe<Part> maybe = orig(SEB_self, param_5606, param_5607, param_5608);

		if (maybe.method_1085()) return maybe;
		// else, check if we clicked on an emitter, which orig(...) can't find
		
		// simpler check - we only care if you click in the hex containing an emitter
		// (could make this work by changing the graphic so emitters take up the entire hex...)

		var hex = getHexFromPoint(param_5606, param_5607);
		maybe = (Maybe<Part>)struct_18.field_1431;
		foreach (Part part in param_5608.Where(x => emitterPartTypes.Contains(x.method_1159())))
		{
			bool partWasClicked = part.method_1161() == hex;
			if (partWasClicked) maybe = (Maybe<Part>)part;
		}
		return maybe;
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
		enum_144 instructionCategory = ret.field_2548;
		bool partTypeFlag = emitterPartTypes.Contains(partType);

		//////////////////////////////////////////////////////////////////////////////////////will need to change the decoding, probably

		//decode the instruction, if needed
		if (partTypeFlag && part.field_2703 == GrabDropDecoding)
		{
			//decode the emitter instruction for the grab/drop-instruction phase
			switch (instructionCategory)
			{
				case (enum_144)1: // PISTON
				case (enum_144)3: // PIVOT
				case (enum_144)5: // GRAB/DROP
					ret = class_169.field_1663; // GRAB
					break;
				case (enum_144)0: // NOOP
				case (enum_144)2: // ROTATE
				case (enum_144)4: // TRACK
				default:
					break;
			}
		}
		else if (partTypeFlag && part.field_2703 == MovementDecoding)
		{
			//decode the emitter instruction for the movement-instruction phase
			switch (instructionCategory)
			{
				case (enum_144)1: // PISTON
					ret = class_169.field_1653; // NOOP
					break;
				case (enum_144)0: // NOOP
				case (enum_144)2: // ROTATE
				case (enum_144)3: // PIVOT
				case (enum_144)4: // TRACK
				case (enum_144)5: // GRAB/DROP
				default:
					break;
			}
		}
		else
		{
			//either it's NOT an emitter, or we want the literal instruction
			//so, don't decode
		}
		//return the decode instruction
		return ret;
	}

	public override void PostLoad()
	{
		//
	}
}