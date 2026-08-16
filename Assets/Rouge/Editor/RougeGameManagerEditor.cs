using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RougeGameManager))]
public class RougeGameManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "skillConfig",
            "playerContactDamage",
            "playerHitInvincibilityDuration",
            "playerContactPadding",
            "playerHitRepulseRadius",
            "playerHitRepulseForce",
            "playerHitRepulseLift",
            "towerBalance",
            "enemyBalance",
            "bossBalance",
            "tacticalSkillBalance");
        EditorGUILayout.HelpBox(
            "Tower/enemy/Boss values are edited in the filtered Tower Defense Balance window.",
            MessageType.Info);
        if (GUILayout.Button("Open Tower Defense Balance", GUILayout.Height(28f)))
            RougeTowerDefenseBalanceEditor.Open();
        EditorGUILayout.Space(8f);

        SerializedProperty skillConfigProperty = serializedObject.FindProperty("skillConfig");
        if (skillConfigProperty != null)
        {
            EditorGUILayout.LabelField("Skill Config", EditorStyles.boldLabel);
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("AutoShoot"), "Auto Shoot");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("PlayerContact"), "Player Contact");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("LeapSmash"), "Leap Smash");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("LightPillar"), "Light Pillar Strike");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("BombThrow"), "Bomb Throw");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("LaserBeam"), "Laser Beam");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("MeleeSlash"), "Melee Slash");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("Shockwave"), "Shockwave");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("MeteorRain"), "Meteor Rain");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("IceZone"), "Ice Zone");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("PoisonBottle"), "Poison Bottle");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("Dash"), "Whirlwind");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("OrbitBall"), "Orbit Ball");
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("Skateboard"), "Skateboard");
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawSkillConfig(SerializedProperty skillProperty, string label)
    {
        if (skillProperty == null)
        {
            return;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        skillProperty.isExpanded = EditorGUILayout.Foldout(skillProperty.isExpanded, label, true);
        if (skillProperty.isExpanded)
        {
            EditorGUI.indentLevel++;

            SerializedProperty presentationProperty = skillProperty.FindPropertyRelative("Presentation");
            if (presentationProperty != null)
            {
                DrawSectionHeader("Behavior");
                DrawPresentation(presentationProperty);
            }

            DrawEffectsSection(skillProperty, "Effects", "Effects");
            DrawEffectsSection(skillProperty, "FinisherEffects", "Finisher Effects");
            DrawEffectsSection(skillProperty, "FinaleSlamEffects", "Finale Slam Effects");
            DrawEffectsSection(skillProperty, "RideEffects", "Ride Effects");
            DrawEffectsSection(skillProperty, "WhirlwindEffects", "Whirlwind Effects");

            EditorGUILayout.Space(4f);
            DrawSectionHeader("Parameters");
            DrawRemainingFields(skillProperty, "Presentation", "Effects", "FinisherEffects", "FinaleSlamEffects", "RideEffects", "WhirlwindEffects");

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(3f);
    }

    private static void DrawPresentation(SerializedProperty presentationProperty)
    {
        SerializedProperty displayName = presentationProperty.FindPropertyRelative("DisplayName");
        SerializedProperty triggerLabel = presentationProperty.FindPropertyRelative("TriggerLabel");
        SerializedProperty enabledProperty = presentationProperty.FindPropertyRelative("Enabled");
        SerializedProperty activationKey = presentationProperty.FindPropertyRelative("ActivationKey");
        SerializedProperty executionType = presentationProperty.FindPropertyRelative("ExecutionType");
        SerializedProperty sustainPriority = presentationProperty.FindPropertyRelative("SustainPriority");
        SerializedProperty isPassive = presentationProperty.FindPropertyRelative("IsPassive");

        if (isPassive != null && isPassive.boolValue && executionType.enumValueIndex != (int)SkillExecutionType.Passive)
        {
            executionType.enumValueIndex = (int)SkillExecutionType.Passive;
        }

        if (enabledProperty != null)
        {
            EditorGUILayout.PropertyField(enabledProperty, new GUIContent("Enabled"));
        }

        EditorGUILayout.PropertyField(displayName);
        EditorGUILayout.PropertyField(triggerLabel);
        EditorGUILayout.PropertyField(executionType, new GUIContent("Skill Type"));

        SkillExecutionType currentType = (SkillExecutionType)executionType.enumValueIndex;
        if (isPassive != null)
        {
            isPassive.boolValue = currentType == SkillExecutionType.Passive;
        }

        if (currentType != SkillExecutionType.Passive)
        {
            EditorGUILayout.PropertyField(activationKey);
        }

        if (currentType == SkillExecutionType.Sustained)
        {
            EditorGUILayout.PropertyField(sustainPriority, new GUIContent("Priority"));
        }
        else if (sustainPriority != null)
        {
            sustainPriority.intValue = 0;
        }
    }

    private static void DrawEffects(SerializedProperty effectsProperty)
    {
        SerializedProperty tagsProperty = effectsProperty.FindPropertyRelative("Tags");
        EditorGUILayout.PropertyField(tagsProperty);

        SkillHitEffectTag tags = (SkillHitEffectTag)tagsProperty.intValue;
        if (tags == SkillHitEffectTag.None)
        {
            return;
        }

        if ((tags & SkillHitEffectTag.Knockback) != 0)
        {
            DrawSectionHeader("Knockback");
            EditorGUILayout.PropertyField(effectsProperty.FindPropertyRelative("KnockbackCenter"));
            EditorGUILayout.PropertyField(effectsProperty.FindPropertyRelative("KnockbackForce"));
        }

        if ((tags & SkillHitEffectTag.Launch) != 0)
        {
            DrawSectionHeader("Launch");
            EditorGUILayout.PropertyField(effectsProperty.FindPropertyRelative("LaunchHeight"));
            EditorGUILayout.PropertyField(effectsProperty.FindPropertyRelative("LaunchLandingRadius"));
        }

        if ((tags & SkillHitEffectTag.Poison) != 0)
        {
            DrawSectionHeader("Poison");
            EditorGUILayout.PropertyField(effectsProperty.FindPropertyRelative("PoisonSpreadRadius"));
        }

        if ((tags & SkillHitEffectTag.Slow) != 0)
        {
            DrawSectionHeader("Slow");
            EditorGUILayout.PropertyField(effectsProperty.FindPropertyRelative("SlowPercent"));
            EditorGUILayout.PropertyField(effectsProperty.FindPropertyRelative("SlowDuration"));
        }

        if ((tags & SkillHitEffectTag.Curse) != 0)
        {
            DrawSectionHeader("Curse");
            EditorGUILayout.PropertyField(effectsProperty.FindPropertyRelative("CurseExplosionDamage"));
            EditorGUILayout.PropertyField(effectsProperty.FindPropertyRelative("CurseExplosionRadius"));
        }

        if ((tags & SkillHitEffectTag.Burn) != 0)
        {
            DrawSectionHeader("Burn");
            EditorGUILayout.PropertyField(effectsProperty.FindPropertyRelative("BurnDamage"));
            EditorGUILayout.PropertyField(effectsProperty.FindPropertyRelative("BurnDuration"));
        }
    }

    private static void DrawEffectsSection(SerializedProperty skillProperty, string propertyName, string label)
    {
        SerializedProperty effectsProperty = skillProperty.FindPropertyRelative(propertyName);
        if (effectsProperty == null)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        DrawSectionHeader(label);
        DrawEffects(effectsProperty);
    }

    private static void DrawRemainingFields(SerializedProperty parentProperty, params string[] excludedNames)
    {
        SerializedProperty iterator = parentProperty.Copy();
        SerializedProperty endProperty = iterator.GetEndProperty();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProperty))
        {
            enterChildren = false;
            if (iterator.depth != parentProperty.depth + 1)
            {
                continue;
            }

            bool isExcluded = false;
            for (int i = 0; i < excludedNames.Length; i++)
            {
                if (iterator.name == excludedNames[i])
                {
                    isExcluded = true;
                    break;
                }
            }

            if (isExcluded)
            {
                continue;
            }

            EditorGUILayout.PropertyField(iterator, true);
        }
    }

    private static void DrawSectionHeader(string title)
    {
        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }
}

[CustomEditor(typeof(RougeCameraFollow))]
public sealed class RougeCameraFollowEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        RougeCameraFollow follow = (RougeCameraFollow)target;
        if (follow.movementBounds != null)
        {
            EditorGUILayout.HelpBox("The visible camera footprint is inset automatically as zoom changes. Select the bounds and drag one of the four cyan edge handles.",
                MessageType.Info);
            if (GUILayout.Button("Select / Edit Camera Bounds", GUILayout.Height(28f)))
            {
                Selection.activeGameObject = follow.movementBounds.gameObject;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Drag the four cyan edge handles in Scene, or assign a RougeCameraBounds object.",
                MessageType.Info);
        }
    }

    private void OnSceneGUI()
    {
        RougeCameraFollow follow = (RougeCameraFollow)target;
        if (follow.movementBounds != null) return;
        Vector3 center = new Vector3(follow.fallbackBoundsCenter.x, follow.transform.position.y,
            follow.fallbackBoundsCenter.y);
        Vector3 size = new Vector3(Mathf.Max(1f, follow.fallbackBoundsSize.x), 0.1f,
            Mathf.Max(1f, follow.fallbackBoundsSize.y));
        if (!RougeCameraBoundsHandleUtility.DrawXZ(ref center, ref size, Matrix4x4.identity)) return;
        Undo.RecordObject(follow, "Resize Camera Movement Bounds");
        follow.fallbackBoundsCenter = new Vector2(center.x, center.z);
        follow.fallbackBoundsSize = new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.z));
        EditorUtility.SetDirty(follow);
    }
}

[CustomEditor(typeof(RougeCameraBounds))]
public sealed class RougeCameraBoundsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox(
            "This rectangle is the visible map boundary. Drag any of the four large cyan edge handles; runtime camera limits adapt to zoom.",
            MessageType.Info);
        if (GUILayout.Button("Frame Bounds In Scene", GUILayout.Height(28f)))
        {
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }

    private void OnSceneGUI()
    {
        RougeCameraBounds cameraBounds = (RougeCameraBounds)target;
        BoxCollider box = cameraBounds.GetComponent<BoxCollider>();
        if (box == null) return;

        Vector3 center = box.center;
        Vector3 size = box.size;
        if (!RougeCameraBoundsHandleUtility.DrawXZ(ref center, ref size,
                cameraBounds.transform.localToWorldMatrix)) return;

        Undo.RecordObject(box, "Resize Camera Movement Bounds");
        box.center = center;
        box.size = new Vector3(Mathf.Max(1f, size.x), Mathf.Max(0.1f, size.y), Mathf.Max(1f, size.z));
        EditorUtility.SetDirty(box);
    }
}

internal static class RougeCameraBoundsHandleUtility
{
    private static readonly Color OutlineColor = new Color(0.08f, 0.92f, 1f, 1f);
    private static readonly Color FillColor = new Color(0.08f, 0.72f, 1f, 0.055f);

    internal static bool DrawXZ(ref Vector3 center, ref Vector3 size, Matrix4x4 matrix)
    {
        size.x = Mathf.Max(1f, size.x);
        size.z = Mathf.Max(1f, size.z);
        float halfX = size.x * 0.5f;
        float halfZ = size.z * 0.5f;
        float y = center.y + Mathf.Max(0.05f, size.y * 0.5f);
        float left = center.x - halfX;
        float right = center.x + halfX;
        float back = center.z - halfZ;
        float forward = center.z + halfZ;

        Vector3 leftPoint = new Vector3(left, y, center.z);
        Vector3 rightPoint = new Vector3(right, y, center.z);
        Vector3 backPoint = new Vector3(center.x, y, back);
        Vector3 forwardPoint = new Vector3(center.x, y, forward);
        Vector3[] corners =
        {
            new Vector3(left, y, back),
            new Vector3(left, y, forward),
            new Vector3(right, y, forward),
            new Vector3(right, y, back)
        };

        float leftSize = GetHandleSize(matrix, leftPoint);
        float rightSize = GetHandleSize(matrix, rightPoint);
        float backSize = GetHandleSize(matrix, backPoint);
        float forwardSize = GetHandleSize(matrix, forwardPoint);
        Vector3 movedLeft;
        Vector3 movedRight;
        Vector3 movedBack;
        Vector3 movedForward;
        bool changed;
        using (new Handles.DrawingScope(OutlineColor, matrix))
        {
            Handles.DrawSolidRectangleWithOutline(corners, FillColor, OutlineColor);
            Handles.DrawAAPolyLine(6f, corners[0], corners[1], corners[2], corners[3], corners[0]);
            EditorGUI.BeginChangeCheck();
            movedLeft = Handles.Slider(leftPoint, Vector3.right, leftSize, Handles.CubeHandleCap, 0f);
            movedRight = Handles.Slider(rightPoint, Vector3.right, rightSize, Handles.CubeHandleCap, 0f);
            movedBack = Handles.Slider(backPoint, Vector3.forward, backSize, Handles.CubeHandleCap, 0f);
            movedForward = Handles.Slider(forwardPoint, Vector3.forward, forwardSize, Handles.CubeHandleCap, 0f);
            changed = EditorGUI.EndChangeCheck();
        }
        if (!changed) return false;

        float newLeft = Mathf.Min(movedLeft.x, movedRight.x - 1f);
        float newRight = Mathf.Max(movedRight.x, newLeft + 1f);
        float newBack = Mathf.Min(movedBack.z, movedForward.z - 1f);
        float newForward = Mathf.Max(movedForward.z, newBack + 1f);
        center.x = (newLeft + newRight) * 0.5f;
        center.z = (newBack + newForward) * 0.5f;
        size.x = newRight - newLeft;
        size.z = newForward - newBack;
        return true;
    }

    private static float GetHandleSize(Matrix4x4 matrix, Vector3 localPoint)
    {
        Vector3 worldPoint = matrix.MultiplyPoint3x4(localPoint);
        return Mathf.Max(0.55f, HandleUtility.GetHandleSize(worldPoint) * 0.13f);
    }
}
