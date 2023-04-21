
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using WorldSyncAAC.AnimatorAsCode.V0;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Juzo.WorldSyncGenerator
{

    public class WorldSyncGenerator : MonoBehaviour
    {
        public VRCAvatarDescriptor avatar;
        public AnimatorController assetContainer;

        public string assetKey;
        
        [Header("This will determine where the object will be dropped. Leave empty to target the avatar root.")]
        public GameObject dropTarget;

    }
    

     [CustomEditor(typeof(WorldSyncGenerator), true)]
     public class WorldSyncGeneratorEditor : Editor
     {
        private const string SystemName = "WorldSyncGenerator";
        private WorldSyncGenerator worldSync;
        private GameObject worldDropRoot;
        private GameObject worldSyncRoot;
        private GameObject resetTarget;
        private AacFlBase aac;
        const HideFlags embedHideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.NotEditable;

        static List<string> axisBinary = new List<string>{ "X", "Y", "Z" };
        static List<string> sizeBinary = new List<string>{ "Macro", "Coarse", "Precise"};



        private void InitializeAAC()
        {   
            worldSync = (WorldSyncGenerator) target;
            aac = WorldSyncTemplate.AnimatorAsCode(SystemName, worldSync.avatar, worldSync.assetContainer, worldSync.assetKey, WorldSyncTemplate.Options().WriteDefaultsOff());
            worldDropRoot = new GameObject("World Drop");
            worldSyncRoot = new GameObject("___WorldSyncFSM");
            worldDropRoot.transform.parent = worldSync.avatar.transform;
            worldSyncRoot.transform.parent = worldSync.avatar.transform;
            resetTarget = new GameObject("___WorldSyncResetTarget");
            resetTarget.transform.parent = worldSync.dropTarget ? worldSync.dropTarget.transform : worldSync.avatar.transform;
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Welcome to the WorldObject setup script! " +
                "The Create button below will add a few parameters and two new layers to your Fx controller. " +
                "It will also create several animations in your animation controller, which will be used to move your object around. " +
                "Once it's set up, feel free to take a look around. Don't worry about breaking anything. " +
                "If your setup is broken, just click the button again to regenerate everything to the correct state.".Trim(), MessageType.None);

            this.DrawDefaultInspector();


            if (GUILayout.Button("Create"))
            {
                Create();
            }
            if (GUILayout.Button("Remove"))
            {
                Remove();
            }
        }

        public void Create()
        {
            InitializeAAC();
            createInitialGameObjectStructure();
        }

        public void createInitialGameObjectStructure()
        {
            GameObject parentObject = null;
            foreach(string size in sizeBinary){
                foreach(string axis in axisBinary){
                    GameObject item = new GameObject(size + "_" + axis);
                    if(parentObject != null){
                    item.transform.parent = parentObject.transform;
                    }
                    else{
                        item.transform.parent = worldSyncRoot.transform;
                    }
                    parentObject = item;
                }
            }

            GameObject item2 = new GameObject("FSM_Base");
            item2.transform.parent = worldSyncRoot.transform;
            parentObject = item2;
            foreach(string axis in axisBinary){
                GameObject fsmItem = new GameObject("FSM_" + axis);
                fsmItem.transform.parent = parentObject.transform;

            }
        }

        public void Remove() 
        {

        }
     }
}