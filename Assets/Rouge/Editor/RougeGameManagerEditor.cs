using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RougeGameManager))]
public class RougeGameManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(serializedObject, "m_Script", "skillConfig");
        EditorGUILayout.Space(8f);

        SerializedProperty skillConfigProperty = serializedObject.FindProperty("skillConfig");
        if (skillConfigProperty != null)
        {
            EditorGUILayout.LabelField("Skill Config", EditorStyles.boldLabel);
            DrawSkillConfig(skillConfigProperty.FindPropertyRelative("AutoShoot"), "Auto Shoot");
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

            SerializedProperty effectsProperty = skillProperty.FindPropertyRelative("Effects");
            if (effectsProperty != null)
            {
                EditorGUILayout.Space(4f);
                DrawSectionHeader("Effects");
                DrawEffects(effectsProperty);
            }

            EditorGUILayout.Space(4f);
            DrawSectionHeader("Parameters");
            DrawRemainingFields(skillProperty, "Presentation", "Effects");

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(3f);
    }

    private static void DrawPresentation(SerializedProperty presentationProperty)
    {
        SerializedProperty displayName = presentationProperty.FindPropertyRelative("DisplayName");
        SerializedProperty triggerLabel = presentationProperty.FindPropertyRelative("TriggerLabel");
        SerializedProperty activationKey = presentationProperty.FindPropertyRelative("ActivationKey");
        SerializedProperty executionType = presentationProperty.FindPropertyRelative("ExecutionType");
        SerializedProperty sustainPriority = presentationProperty.FindPropertyRelative("SustainPriority");
        SerializedProperty isPassive = presentationProperty.FindPropertyRelative("IsPassive");

        if (isPassive != null && isPassive.boolValue && executionType.enumValueIndex != (int)SkillExecutionType.Passive)
        {
            executionType.enumValueIndex = (int)SkillExecutionType.Passive;
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