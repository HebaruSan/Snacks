﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using KSP.UI;

/**
The MIT License (MIT)
Copyright (c) 2014-2019 by Michael Billard
 

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 * */
namespace Snacks
{
    public class SnackAppView : Window<SnackAppView>
    {
        public string exemptKerbals = "Ted";

        private Vector2 scrollPos = new Vector2();
        private Vector2 scrollPosButtons = new Vector2();
        private int selectedBody = 0;
        private GUILayoutOption[] flightWindowLeftPaneOptions = new GUILayoutOption[] { GUILayout.Width(200) };
        private GUILayoutOption[] flightWindowRightPaneOptions = new GUILayoutOption[] { GUILayout.Width(300) };

        private int partCount = 0;
        private bool simulationComplete = false;
        private string simulationResults = string.Empty;
        private int previousCrewCount = 0;
        private int currentCrewCount = -1;
        private List<Snackshot> snackshots = null;
        private SnackSimThread snackThread = null;
        private bool convertersAssumedActive = false;
        int crewCapacity = 0;

        public SnackAppView() :
        base("Vessel Status", 500, 500)
        {
            Resizable = false;
        }

        public override void SetVisible(bool newValue)
        {
            base.SetVisible(newValue);

            if (newValue)
            {
                if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedScene == GameScenes.SPACECENTER)
                {
                    SnacksScenario.Instance.UpdateSnapshots();
                    SnacksScenario.onSnackTime.Add(onSnackTime);
                }
                else if (HighLogic.LoadedSceneIsEditor)
                {
                    snackshots = new List<Snackshot>();

                    snackThread = new SnackSimThread(new Mutex(), new List<SimSnacks>());
                    snackThread.OnSimulationComplete = OnSimulationComplete;
                    snackThread.Start();
                }

                exemptKerbals = SnacksScenario.Instance.exemptKerbals;
                SnacksScenario.Instance.SetExemptCrew(exemptKerbals);
            }

            else
            {
                if (snackThread != null)
                    snackThread.Stop();

                if (SnacksScenario.Instance == null)
                    return;
                SnacksScenario.Instance.exemptKerbals = exemptKerbals;
                if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedScene == GameScenes.SPACECENTER)
                {
                    SnacksScenario.onSnackTime.Remove(onSnackTime);

                    if (SnacksScenario.Instance != null && SnacksScenario.Instance.threadPool != null)
                        SnacksScenario.Instance.threadPool.StopAllJobs();
                }
            }
        }

        private void onSnackTime()
        {
            SnacksScenario.Instance.UpdateSnapshots();
        }

        protected override void DrawWindowContents(int windowId)
        {
            if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
                drawSpaceCenterWindow();
            else if (HighLogic.LoadedSceneIsEditor == false)
                drawFlightWindow();
            else
                drawEditorWindow();
        }

        public void drawEditorWindow()
        {
            //Rerun sim button
            if (GUILayout.Button("Rerun Simulator"))
            {
                //Reset crew count so that we'll trigger a rebuild of the simulator.
                currentCrewCount = -1;
            }

            scrollPos = GUILayout.BeginScrollView(scrollPos);//, GUILayout.Height(300), GUILayout.Width(500));

            //Setup simulator
            setupSimulatorIfNeeded();

            //Update status
            snackThread.mutex.WaitOne();
            if (simulationComplete)
                formatSimulationResults();
            snackThread.mutex.ReleaseMutex();
            GUILayout.Label(simulationResults);

            GUILayout.EndScrollView();
        }

        private void setupSimulatorIfNeeded()
        {
            //If the vessel parts have changed or crew count has changed then run a new simulation.
            ShipConstruct ship = EditorLogic.fetch.ship;
            VesselCrewManifest manifest = CrewAssignmentDialog.Instance.GetManifest();
            int consumerCount = SnacksScenario.Instance.resourceProcessors.Count;
            List<BaseResourceProcessor> resourceProcessors = SnacksScenario.Instance.resourceProcessors;
            List<ProcessedResource> consumerResources;
            int resourceCount;
            int resourceIndex;
            string resourceName;
            Snackshot snackshot;

            if (manifest != null)
                currentCrewCount = manifest.CrewCount;

            //Get crew capacity
            crewCapacity = 0;
            int partCrewCapacity = 0;
            for (int index = 0; index < ship.parts.Count; index++)
            {
                if (ship.parts[index].partInfo.partConfig.HasValue("CrewCapacity"))
                {
                    int.TryParse(ship.parts[index].partInfo.partConfig.GetValue("CrewCapacity"), out partCrewCapacity);
                    crewCapacity += partCrewCapacity;
                }
            }

            if (ship.parts.Count != partCount || currentCrewCount != previousCrewCount)
            {
                previousCrewCount = currentCrewCount;
                partCount = ship.parts.Count;

                //No parts? Nothing to do.
                if (partCount == 0)
                {
                    snackshots.Clear();
                    simulationResults = "<color=yellow><b>Vessel has no crewed parts to simulate.</b></color>";
                    simulationComplete = false;
                }
                else if (currentCrewCount == 0)
                {
                    snackshots.Clear();
                    simulationResults = "<color=yellow><b>Vessel needs crew to run simulation.</b></color>";
                    simulationComplete = false;
                }

                //Clear existing simulation if any
                snackThread.ClearJobs();

                SimSnacks simSnacks = SimSnacks.CreateSimulator(ship);
                if (simSnacks != null)
                {
                    simulationComplete = false;
                    simulationResults = "<color=white><b>Simulation in progress, please wait...</b></color>";
                    snackshots.Clear();

                    //Get consumer resource lists
                    for (int consumerIndex = 0; consumerIndex < consumerCount; consumerIndex++)
                    {
                        resourceProcessors[consumerIndex].AddConsumedAndProducedResources(currentCrewCount, simSnacks.secondsPerCycle, simSnacks.consumedResources, simSnacks.producedResources);

                        //First check input list for resources to add to the snapshots window
                        consumerResources = resourceProcessors[consumerIndex].inputList;
                        resourceCount = consumerResources.Count;
                        for (resourceIndex = 0; resourceIndex < resourceCount; resourceIndex++)
                        {
                            resourceName = consumerResources[resourceIndex].resourceName;

                            if (consumerResources[resourceIndex].showInSnapshot && simSnacks.resources.ContainsKey(resourceName))
                            {
                                snackshot = new Snackshot();
                                snackshot.showTimeRemaining = true;
                                snackshot.resourceName = consumerResources[resourceIndex].resourceName;
                                snackshot.amount = simSnacks.resources[resourceName].amount;
                                snackshot.maxAmount = simSnacks.resources[resourceName].maxAmount;

                                //Add to snackshots
                                snackshots.Add(snackshot);
                            }
                        }

                        //Next check outputs
                        consumerResources = resourceProcessors[consumerIndex].outputList;
                        resourceCount = consumerResources.Count;
                        for (resourceIndex = 0; resourceIndex < resourceCount; resourceIndex++)
                        {
                            resourceName = consumerResources[resourceIndex].resourceName;

                            if (consumerResources[resourceIndex].showInSnapshot && simSnacks.resources.ContainsKey(resourceName))
                            {
                                snackshot = new Snackshot();
                                snackshot.showTimeRemaining = true;
                                snackshot.resourceName = consumerResources[resourceIndex].resourceName;
                                snackshot.amount = simSnacks.resources[resourceName].amount;
                                snackshot.maxAmount = simSnacks.resources[resourceName].maxAmount;

                                //Add to snackshots
                                snackshots.Add(snackshot);
                            }
                        }
                    }

                    //Give mods a chance to add custom converters not already covered by Snacks.
                    SimulatorContext context = new SimulatorContext();
                    context.shipConstruct = ship;
                    context.simulatedVesselType = SimulatedVesselTypes.simEditor;
                    SnacksScenario.onSimulatorCreated.Fire(simSnacks, context);

                    //Now start the simulation
                    snackThread.AddJob(simSnacks);
                }
                else
                {
                    simulationResults = "<color=yellow><b>Vessel has no crewed parts to simulate.</b></color>";
                }
            }
        }

        private void formatSimulationResults()
        {
            StringBuilder simResults = new StringBuilder();
            int count = snackshots.Count;
            Snackshot snackshot;

            //current/max crew
            simResults.AppendLine("<color=white>Crew: " + currentCrewCount + "/" + crewCapacity + "</color>");

            //Snackshot list
            for (int index = 0; index < count; index++)
            {
                snackshot = snackshots[index];
                simResults.AppendLine(snackshot.GetStatusDisplay());
            }

            //Converter assumption
            if (convertersAssumedActive)
                simResults.AppendLine("<color=orange>Assumes converters are active; be sure to turn them on.</color>");

            simulationResults = simResults.ToString();
        }

        private void OnSimulationComplete(SimSnacks simSnacks)
        {
            simulationComplete = true;

            //Snackshot list
            int count = snackshots.Count;
            Snackshot snackshot;
            for (int index = 0; index < count; index++)
            {
                snackshot = snackshots[index];

                if (simSnacks.consumedResourceDurations.ContainsKey(snackshot.resourceName))
                {
                    snackshot.isSimulatorRunning = false;
                    snackshot.estimatedTimeRemaining = simSnacks.consumedResourceDurations[snackshot.resourceName];
                }
            }

            convertersAssumedActive = simSnacks.convertersAssumedActive;
        }

        public void drawSpaceCenterWindow()
        {
            GUILayout.Label("<color=white><b>Exempt Kerbals:</b> separate names by semicolon, first name only</color>");
            GUILayout.Label("<color=yellow>These kerbals won't consume Snacks and won't suffer penalties from a lack of Snacks.</color>");
            if (string.IsNullOrEmpty(exemptKerbals))
                exemptKerbals = string.Empty;
            exemptKerbals = GUILayout.TextField(exemptKerbals);

            if (SnacksProperties.DebugLoggingEnabled)
            {
                if (GUILayout.Button("Snack Time!"))
                {
                    SnacksScenario.Instance.RunSnackCyleImmediately(SnacksScenario.GetSecondsPerDay() / SnacksProperties.MealsPerDay);
                }
            }

            drawFlightWindow();
        }

        public void drawFlightWindow()
        {
            Dictionary<Vessel, VesselSnackshot> snapshotMap = SnacksScenario.Instance.snapshotMap;
            VesselSnackshot vesselSnackshot;
            List<Vessel> keys = snapshotMap.Keys.ToList();
            int count = keys.Count;
            List<CelestialBody> bodies = FlightGlobals.Bodies;
            Dictionary<string, double> resourceDurations = null;
            int snackShotCount = 0;
            Snackshot snackshot;
            bool convertersAssumedActive;

            //Update resource durations
            SnacksScenario.Instance.threadPool.LockResourceDurations();
            for (int index = 0; index < keys.Count; index++)
            {
                resourceDurations = SnacksScenario.Instance.threadPool.GetVesselResourceDurations(keys[index]);
                if (resourceDurations != null)
                {
                    convertersAssumedActive = SnacksScenario.Instance.threadPool.ConvertersAssumedActive(keys[index]);

                    SnacksScenario.Instance.threadPool.RemoveVesselResourceDurations(keys[index]);

                    vesselSnackshot = snapshotMap[keys[index]];
                    vesselSnackshot.convertersAssumedActive = convertersAssumedActive;
                    snackShotCount = vesselSnackshot.snackshots.Count;
                    for (int snackShotIndex = 0; snackShotIndex < snackShotCount; snackShotIndex++)
                    {
                        snackshot = vesselSnackshot.snackshots[snackShotIndex];
                        if (resourceDurations.ContainsKey(snackshot.resourceName))
                        {
                            snackshot.estimatedTimeRemaining = resourceDurations[snackshot.resourceName];
                            snackshot.isSimulatorRunning = false;
                        }
                    }
                }
            }
            SnacksScenario.Instance.threadPool.UnlockResourceDurations();

            //Draw left pane
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical();
            scrollPosButtons = GUILayout.BeginScrollView(scrollPosButtons, flightWindowLeftPaneOptions);
            selectedBody = FlightGlobals.currentMainBody.flightGlobalsIndex;
            if (SnacksScenario.Instance.bodyVesselCountMap.Keys.Count > 0)
            {
                int bodyCount = bodies.Count;
                for (int bodyIndex = 0; bodyIndex < bodyCount; bodyIndex++)
                {
                    //Skip body if it has new crewed vessels
                    if (!SnacksScenario.Instance.bodyVesselCountMap.ContainsKey(bodyIndex) || SnacksScenario.Instance.bodyVesselCountMap[bodyIndex] == 0)
                        continue;

                    //Record the selected body index
                    if (GUILayout.Button(bodies[bodyIndex].bodyName))
                    {
                        selectedBody = bodyIndex;
                    }
                }
            }
            else
            {
                GUILayout.Label("<color=white>No crewed vessels found on or around any world or in solar orbit.</color>");
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            //Draw right pane
            GUILayout.BeginVertical();
            scrollPos = GUILayout.BeginScrollView(scrollPos, flightWindowRightPaneOptions);
            GUILayout.Label("<color=lightBlue><b>" + bodies[selectedBody].bodyName + "</b></color>");
            count = keys.Count;
            string statusDisplay;
            for (int index = 0; index < count; index++)
            {
                vesselSnackshot = snapshotMap[keys[index]];

                //Skip if vessel's planetary body doesn't match the filter.
                if (vesselSnackshot.bodyID != selectedBody)
                    continue;

                //Get status
                statusDisplay = vesselSnackshot.GetStatusDisplay();
                if (vesselSnackshot.convertersAssumedActive)
                    statusDisplay = statusDisplay + "<color=orange>Assumes converters are active; be sure to turn them on.</color>";

                //Print status
                GUILayout.Label(statusDisplay);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }
    }
}
