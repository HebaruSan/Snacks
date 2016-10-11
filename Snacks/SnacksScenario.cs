﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KSP;
using KSP.IO;

namespace Snacks
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER, GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class SnacksScenario : ScenarioModule
    {
        public static SnacksScenario Instance;
        public Dictionary<string, int> sciencePenalties = new Dictionary<string, int>();

        public override void OnAwake()
        {
            base.OnAwake();
            Instance = this;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            ConfigNode[] penalties = node.GetNodes("SCIENCE_PENALTY");
            foreach (ConfigNode penaltyNode in penalties)
            {
                sciencePenalties.Add(penaltyNode.GetValue("vesselID"), int.Parse(penaltyNode.GetValue("amount")));
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            ConfigNode penaltyNode;
            foreach (string key in sciencePenalties.Keys)
            {
                penaltyNode = new ConfigNode("SCIENCE_PENALTY");
                penaltyNode.AddValue("vesselID", key);
                penaltyNode.AddValue("amount", sciencePenalties[key].ToString());
                node.AddNode(penaltyNode);
            }
        }
    }
}
