
#if UNITY_EDITOR
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using WorldSyncAAC.AnimatorAsCode.V0;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using UnityEngine.Animations;
using System.Reflection;

namespace Juzo.WorldSyncGenerator
{

    public class WorldSyncGenerator : MonoBehaviour
    {
        public VRCAvatarDescriptor avatar;
        public AnimatorController assetContainer;

        public string assetKey;
        
        [Header("Where to drop the object. Leave empty to target avatar root.")]
        public GameObject dropTarget;

    }
    

     [CustomEditor(typeof(WorldSyncGenerator), true)]
     public class WorldSyncGeneratorEditor : Editor
     {
        private const string SystemName = "WorldSyncGenerator";
        private WorldSyncGenerator worldSync;
        private GameObject worldDropRoot;
        private GameObject worldSyncRoot;
        private GameObject WorldDropContainer;
        private GameObject resetTarget;

        private GameObject toggleObject;
        private AacFlBase aac;

        private AnimatorController animatorController;
        const HideFlags embedHideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.NotEditable;

        static List<string> axisBinary = new List<string>{ "X", "Y", "Z" };
        static List<string> sizeBinary = new List<string>{ "Macro", "Coarse", "Precise"};
        static List<float> validIncrementValues = new List<float>{ 63/127, 32/127, 16/127, 8/127, 4/127, 2/127, 1/127 };


        private void InitializeAAC()
        {   
            worldSync = (WorldSyncGenerator) target;
            aac = WorldSyncTemplate.AnimatorAsCode(SystemName, worldSync.avatar, worldSync.assetContainer, worldSync.assetKey, WorldSyncTemplate.Options().WriteDefaultsOff());
            worldDropRoot = new GameObject("___WorldSync_World Drop");
            worldSyncRoot = new GameObject("___WorldSyncFSM");
            worldDropRoot.transform.parent = worldSync.avatar.transform;
            worldSyncRoot.transform.parent = worldDropRoot.transform;
            resetTarget = new GameObject("___WorldSyncResetTarget");
            resetTarget.transform.parent = worldSync.dropTarget ? worldSync.dropTarget.transform : worldSync.avatar.transform;
            WorldDropContainer = new GameObject("Container");
            WorldDropContainer.transform.parent = worldDropRoot.transform;
            toggleObject = new GameObject("ToggleObject");
            toggleObject.transform.parent = WorldDropContainer.transform;

            var fx = aac.CreateMainFxLayer();
            
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
            CreateWorldSpaceConstraint();
            GameObject finalDepth = CreateInitialGameObjectStructure();
            CreateFiniteStateMachine(finalDepth);
            CreateAnimationsPreciseCoarseMacro();
        }
        public void CreateWorldSpaceConstraint(){
            GameObject world = AssetDatabase.FindAssets("World",new[] {"Assets/WorldSync2/DoNotUse"}).Select(guid => AssetDatabase.GUIDToAssetPath(guid)).Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path)).FirstOrDefault();
            ParentConstraint worldConstraint = worldDropRoot.AddComponent<ParentConstraint>();
            worldConstraint.AddSource(new ConstraintSource{sourceTransform = world.transform, weight = 1});
            worldConstraint.constraintActive = true;
            ScaleConstraint worldScaleConstraint = worldDropRoot.AddComponent<ScaleConstraint>();
            worldScaleConstraint.AddSource(new ConstraintSource{sourceTransform = world.transform, weight = 1});
            worldScaleConstraint.constraintActive = true;            
        }
        public GameObject CreateInitialGameObjectStructure()
        {
            // Objects to be moved with the blendtrees. Macro, Coarse, Precise
            // Macro +-10000, Coarse +-100, Precise +-1
            GameObject parentObject = null;
            GameObject zPrecise = null;
            foreach(string axis in axisBinary){
                foreach(string size in sizeBinary){
                    GameObject item = new GameObject(size + "_" + axis);
                    if(parentObject != null){
                    item.transform.parent = parentObject.transform;
                    }
                    else{
                        item.transform.parent = worldSyncRoot.transform;
                    }
                    parentObject = item;
                    if(size == "Precise" && axis == "Z"){
                        zPrecise = item;
                    }
                }
            }
            
            // Create rotation receiver under fsm_z
            GameObject final_position_with_rotation = new GameObject("finalPositionWithRotation");
            final_position_with_rotation.transform.parent = zPrecise.transform;

            return final_position_with_rotation;
        }

        public void CreateFiniteStateMachine(GameObject finalRotationDepth)
        {
            // Objects to track user position. X, Y, Z at 1/127th scale
            GameObject FSM_Base = new GameObject("FSM_Base");
            FSM_Base.transform.parent = worldSyncRoot.transform;
            GameObject fsmObjectZ = null;

            GameObject parentObject = FSM_Base;
            foreach(string axis in axisBinary){
                GameObject fsmItem = new GameObject("FSM_" + axis);
                fsmItem.transform.parent = parentObject.transform;
                var sender = fsmItem.AddComponent<VRCContactSender>();
                var constraint = fsmItem.AddComponent<PositionConstraint>();
                constraint.constraintActive = true;
                constraint.AddSource(new ConstraintSource{sourceTransform = resetTarget.transform, weight = 1});
                // constraint.translationAxis = axis == "X" ? (Axis.Y | Axis.Z) : axis == "Y" ? (Axis.Z | Axis.X) : (Axis.X | Axis.Y);
                constraint.translationAxis = axis == "X" ? Axis.X : axis == "Y" ? Axis.Y : Axis.Z;
                constraint.weight = 1/127f;
                sender.radius = 0f;
                sender.collisionTags = new List<string>{axis + "sender"};
                parentObject = fsmItem;
                if(axis == "Z"){
                    fsmObjectZ = fsmItem;
                }
            }


            GameObject independentAxisConstraints = new GameObject("IndependentAxisConstraints");
            independentAxisConstraints.transform.parent = FSM_Base.transform;
            Dictionary<String, GameObject> axisSenders = new Dictionary<string, GameObject>();
            foreach(string axis in axisBinary){
                GameObject fsmItem = new GameObject("Independent_" + axis);
                fsmItem.transform.parent = independentAxisConstraints.transform;
                var constraint = fsmItem.AddComponent<PositionConstraint>();
                constraint.constraintActive = true;
                constraint.AddSource(new ConstraintSource{sourceTransform = resetTarget.transform, weight = 1});
                constraint.weight = 1/127f;
                constraint.translationAxis = axis == "X" ? Axis.X : axis == "Y" ? Axis.Y : Axis.Z;
                axisSenders[axis] = fsmItem;                
            }

            // Create the rotationSource
            GameObject rotationSource = new GameObject("RotationSource");
            rotationSource.transform.parent = worldSyncRoot.transform;
            var rotationConstraint = rotationSource.AddComponent<RotationConstraint>();
            rotationConstraint.constraintActive = true;
            ConstraintSource constraintSource = new ConstraintSource{sourceTransform = worldSync.avatar.transform.Find("Armature").transform, weight = 1};
            rotationConstraint.AddSource(constraintSource);
            rotationConstraint.rotationAxis = Axis.Y;

            VRCPhysBoneCollider planeCollider = rotationSource.AddComponent<VRCPhysBoneCollider>();
            planeCollider.shapeType = VRCPhysBoneCollider.ShapeType.Plane;
            planeCollider.position = new Vector3(0,0,3);
            planeCollider.rotation = Quaternion.Euler(0,0,0);

            // Objects to Move for future animations
            var localeTrackedParent = FSM_Base;
            var moverParent = new GameObject("AxisMovers");
            moverParent.transform.parent = FSM_Base.transform;
            
            GameObject animatedObjects = new GameObject("AnimatedObjects");
            animatedObjects.transform.parent = worldSyncRoot.transform;


            foreach(string axis in axisBinary){

                
                GameObject animatedObject = new GameObject("Animated_" + axis);
                animatedObject.transform.parent = animatedObjects.transform;


                GameObject item = new GameObject("Move_aim_" + axis);
                item.transform.parent = moverParent.transform;
                GameObject localeTrackedObject = new GameObject("LocaleTracked_" + axis);
                localeTrackedObject.transform.parent = localeTrackedParent.transform;

                var constraint = localeTrackedObject.AddComponent<PositionConstraint>();
                constraint.constraintActive = true;
                constraint.AddSource(new ConstraintSource{sourceTransform = animatedObject.transform, weight = 1});
                constraint.translationAxis = axis == "X" ? (Axis.Y | Axis.Z) : axis == "Y" ? (Axis.Z | Axis.X) : (Axis.X | Axis.Y);
                localeTrackedParent = localeTrackedObject;

                var aim_constraint = item.AddComponent<AimConstraint>();
                aim_constraint.constraintActive = true;
                aim_constraint.AddSource(new ConstraintSource{sourceTransform = axisSenders[axis].transform, weight = 1});
                aim_constraint.rotationAxis = axis == "X" ? (Axis.Y | Axis.Z) : axis == "Y" ? (Axis.Z | Axis.X) : (Axis.X | Axis.Y);
                aim_constraint.aimVector = axis == "X" ? (Vector3.forward) : axis == "Y" ? (Vector3.forward) : (Vector3.up);
                aim_constraint.upVector = Vector3.up;
                // Create senders and receivers
                var senderObject = new GameObject("Sender_" + axis);
                senderObject.transform.parent = item.transform;
                senderObject.transform.localPosition = axis == "X" ? new Vector3(0.5f,0,0) : axis == "Y" ? new Vector3(0,0,0.5f) : new Vector3(0,0,0.5f);
                var receiverObject = new GameObject("Receiver_aim_" + axis);
                receiverObject.transform.parent = moverParent.transform;
                receiverObject.transform.localPosition = axis == "X" ? new Vector3(0,0,0.5f) : axis == "Y" ? new Vector3(0,-0.5f,0) : new Vector3(0,0.5f,0);
                var sender = senderObject.AddComponent<VRCContactSender>();
                sender.radius = 0f;
                sender.collisionTags = new List<string>{axis + "_aim_sender"};
                var vrcReceiver = receiverObject.AddComponent<VRCContactReceiver>();
                vrcReceiver.collisionTags = new List<string>{axis + "_aim_sender"};
                vrcReceiver.radius = .5f;
                vrcReceiver.localOnly = true;
                vrcReceiver.allowOthers = false;
                vrcReceiver.allowSelf = true;
                vrcReceiver.parameter = axis + "_aim_negative";
            }

            // Create the Receiver structure
            GameObject receivers = new GameObject("Receivers");
            receivers.transform.parent = fsmObjectZ.transform;
            GameObject receiver = new GameObject("Receiver_axis");
            receiver.transform.parent = receivers.transform;

            foreach(string axis in axisBinary){
                var receiverComponent = receiver.AddComponent<VRCContactReceiver>();
                receiverComponent.collisionTags = new List<string>{axis + "sender"};
                receiverComponent.radius = 1/127f;
                receiverComponent.allowSelf = true;
                receiverComponent.allowOthers = false;
                receiverComponent.localOnly = true;
                receiverComponent.receiverType = VRC.Dynamics.ContactReceiver.ReceiverType.Proximity;
                receiverComponent.parameter = axis + "_precise";

                var receiverComponentBool = receiver.AddComponent<VRCContactReceiver>();
                receiverComponentBool.collisionTags = new List<string>{axis + "sender"};
                receiverComponentBool.radius = 1f;
                receiverComponentBool.allowSelf = true;
                receiverComponentBool.allowOthers = false;
                receiverComponentBool.localOnly = true;
                receiverComponentBool.receiverType = VRC.Dynamics.ContactReceiver.ReceiverType.Constant;
                receiverComponentBool.parameter = axis + "_coarse";
            }
            

            // Create remote local swap on container
            ParentConstraint localSwap = WorldDropContainer.AddComponent<ParentConstraint>();
            localSwap.AddSource(new ConstraintSource{sourceTransform = resetTarget.transform, weight = 1});
            localSwap.AddSource(new ConstraintSource{sourceTransform = finalRotationDepth.transform, weight = 0});
            localSwap.constraintActive = true;
            localSwap.weight = 1;

            // Create Rotation Measurements
            GameObject rotationMeasurements = new GameObject("RotationMeasurements");
            rotationMeasurements.transform.parent = worldSyncRoot.transform;
            GameObject rotationMeasurement = new GameObject("RotationMeasurement");
            rotationMeasurement.transform.parent = rotationMeasurements.transform;
            VRCPhysBone rotationBone = rotationMeasurement.AddComponent<VRCPhysBone>();
            rotationBone.endpointPosition = new Vector3(0,0,1);
            rotationBone.pull = 0;
            rotationBone.spring = 1;
            rotationBone.gravity = 0;
            rotationBone.immobile = 1;
            rotationBone.immobileType = VRC.Dynamics.VRCPhysBoneBase.ImmobileType.World;
            rotationBone.parameter = "Rotation";


            GameObject rotationMeasurement2 = new GameObject("RotationMeasurement2");
            rotationMeasurement2.transform.parent = rotationMeasurements.transform;
            VRCPhysBone rotationBone2 = rotationMeasurement2.AddComponent<VRCPhysBone>();
            rotationBone2.endpointPosition = new Vector3(1,0,0);
            rotationBone2.pull = 0;
            rotationBone2.spring = 1;
            rotationBone2.gravity = 0;
            rotationBone2.immobile = 1;
            rotationBone2.immobileType = VRC.Dynamics.VRCPhysBoneBase.ImmobileType.World;
            rotationBone2.parameter = "Rotation2";
            

        }

        public static string GetGameObjectPath(GameObject obj)
        {
            string path = "";
            while (obj.transform.parent != null)
            {
                path = obj.name + path;
                obj = obj.transform.parent.gameObject;
                if (obj.transform.parent != null)
                {
                    path = "/" + path;
                }
            }
            return path;
        }

        public void CreateAnimationsPreciseCoarseMacro(){


            var blendtreeLayer = aac.CreateSupportingFxLayer("__worldSync_" + worldSync.assetKey + "_AxisBlendTrees");
            var param = blendtreeLayer.FloatParameter("__worldSync" + worldSync.assetKey + "/dummyWeight");
            blendtreeLayer.OverrideValue(param, 1);
            BlendTree combinedBlendTree = aac.NewBlendTreeAsRaw();
            combinedBlendTree.blendType = BlendTreeType.Direct;
            combinedBlendTree.blendParameter = param.Name;
            combinedBlendTree.minThreshold = 0;
            combinedBlendTree.maxThreshold = 1;
            combinedBlendTree.children = new ChildMotion[0];
            combinedBlendTree.name = "Combined Movement Axis";
            var blendTreeDefault = blendtreeLayer.NewState("Combined Movement Axis");
            blendTreeDefault.WithAnimation(combinedBlendTree);
            blendTreeDefault.WithWriteDefaultsSetTo(true);
            

            const string positionPropName = "m_LocalPosition";
            foreach(string axis in axisBinary){
                foreach(string size in sizeBinary){
                    AacFlClip clip = aac.NewClip("Pos_Move_" + size + "_" + axis);
                    AacFlClip negclip = aac.NewClip("Neg_Move_" + size + "_" + axis);
                    AacFlClip zeroClip = aac.NewClip("Zero_Move_" + size + "_" + axis);
                    var maxThreshold = size == "Macro" ? 127*127 : size == "Coarse" ? 127 : 1;
                    GameObject animatedObject = GameObject.Find(size + "_" + axis);
                    // twice to handle unity animation rules
                    clip.Animating(anim => anim.Animates(GetGameObjectPath(animatedObject) , typeof(Transform), positionPropName + "." + axis.ToLowerInvariant()).WithOneFrame(maxThreshold));
                    negclip.Animating(anim => anim.Animates(GetGameObjectPath(animatedObject) , typeof(Transform), positionPropName + "." + axis.ToLowerInvariant()).WithOneFrame(-1 * maxThreshold));
                    clip.Animating(anim => anim.Animates(GetGameObjectPath(animatedObject) , typeof(Transform), positionPropName + "." + axis.ToLowerInvariant()).WithOneFrame(maxThreshold));
                    negclip.Animating(anim => anim.Animates(GetGameObjectPath(animatedObject) , typeof(Transform), positionPropName + "." + axis.ToLowerInvariant()).WithOneFrame(-1 * maxThreshold));
                    zeroClip.Animating(anim => anim.Animates(GetGameObjectPath(animatedObject) , typeof(Transform), positionPropName + "." + axis.ToLowerInvariant()).WithOneFrame(0));
                    zeroClip.Animating(anim => anim.Animates(GetGameObjectPath(animatedObject) , typeof(Transform), positionPropName + "." + axis.ToLowerInvariant()).WithOneFrame(0));

                // clipImplodeControlled.Animating(anim => anim.Animates(mover + currentPathGroupPosition, typeof(Transform), $"{positionPropName}.y").WithOneFrame(-1f));
                    BlendTree blendTree = CreateTriBlendTree(blendtreeLayer.FloatParameter("__worldSync" + worldSync.assetKey + "/" + axis + "_" + size), negclip, zeroClip, clip, axis + "_" + size);
                    ChildMotion childMotion = new ChildMotion{motion = blendTree, timeScale = 1, threshold = 0, directBlendParameter=param.Name};
                    combinedBlendTree.children = combinedBlendTree.children.Concat(new []{childMotion}).ToArray();
                }
            }
        }

        public void AddBlendTreeToDirectBlendTree(BlendTree directBlendTree, BlendTree blendTree)
        {
            // Add blendtree to direct blendtree
        }

        private BlendTree CreateTriBlendTree(AacFlFloatParameter controlParameter,AacFlClip minusClip,  AacFlClip zeroClip, AacFlClip oneClip, string name = "")
        {
            BlendTree blendTree = aac.NewBlendTreeAsRaw();
            blendTree.blendType = BlendTreeType.Simple1D;
            blendTree.blendParameter = controlParameter.Name;
            blendTree.minThreshold = -1;
            blendTree.maxThreshold = 1;
            blendTree.children = new []
            {
                new ChildMotion { motion = minusClip.Clip, timeScale = 1, threshold = -1 },
                new ChildMotion { motion = zeroClip.Clip, timeScale = 1, threshold = 0 },
                new ChildMotion { motion = oneClip.Clip, timeScale = 1, threshold = 1 }
            };

            if(name.Length > 0)
            {
                blendTree.name = name;
            }
            return blendTree;
        }
        public void Remove() 
        {   
            GameObject worldDropRoot = GameObject.Find("___WorldSync_World Drop");
            GameObject container = GameObject.Find("ToggleObject");
            GameObject resetTarget = GameObject.Find("___WorldSyncResetTarget");
            Debug.Log("Removing WorldSync " + container.transform.childCount);
            while(container.transform.childCount >0)
            {
                container.transform.GetChild(0).gameObject.transform.parent = worldDropRoot.transform.parent.transform;
            }
            DestroyImmediate(worldDropRoot);
            DestroyImmediate(resetTarget);
            aac.ClearPreviousAssets();
        }
    }
}
#endif