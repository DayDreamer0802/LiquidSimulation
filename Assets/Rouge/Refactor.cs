using System;
using System.IO;
using System.Text.RegularExpressions;
using Unity.Mathematics;

class Program {
    static void Main() {
        string path = @"f:\NewRouge\RedVsBlue\Assets\Rouge\RougeGameManager.cs";
        string text = File.ReadAllText(path);

        string structDef = @"public struct RougeSkillArea
{
    public int Type;
    public float2 Position;
    public float2 Direction;
    public float Radius;
    public float Length;
    public float Damage;
    public float PullForce;
    public float SpinForce;
    public float VerticalForce;
}
public struct RougeBullet";
        text = text.Replace("public struct RougeBullet", structDef);

        var regexFields = new Regex(@"public float TornadoActive;[\s\S]*?public float BlackHoleSpinForce;");
        text = regexFields.Replace(text, @"[ReadOnly] public NativeArray<RougeSkillArea> SkillAreas;
      public int SkillAreaCount;");

        var regexSchedule = new Regex(@"TornadoActive = _tornadoTimer > 0f \? 1f : 0f,[\s\S]*?BlackHoleSpinForce = blackHoleSpinForce,");
        text = regexSchedule.Replace(text, @"SkillAreas = _skillAreasDb,
              SkillAreaCount = _activeSkills.Count,");

        var regexLogic = new Regex(@"if \(TornadoActive > 0\.5f\)[\s\S]*?BlackHoleSpinForce \* weight\);\s*\}\s*\}");
        text = regexLogic.Replace(text, @"for (int s = 0; s < SkillAreaCount; s++)
            {
                RougeSkillArea skill = SkillAreas[s];
                if (skill.Type == 1)
                {
                    float2 diff = skill.Position - pos.xz;
                    float distSq = math.lengthsq(diff);
                    if (distSq < skill.Radius * skill.Radius && distSq > 0.0001f)
                    {
                        float dist = math.sqrt(distSq);
                        float weight = 1f - math.saturate(dist / skill.Radius);
                        float2 dir = diff / dist;
                        float2 tangent = new float2(-dir.y, dir.x);
                        acceleration.xz += dir * (skill.PullForce * weight);
                        acceleration.xz += tangent * (skill.SpinForce * weight);
                        acceleration.y += skill.VerticalForce * weight;
                        health -= skill.Damage * DeltaTime;
                    }
                }
                else if (skill.Type == 2)
                {
                    float2 diff = pos.xz - skill.Position;
                    float distSq = math.lengthsq(diff);
                    if (distSq < skill.Radius * skill.Radius && distSq > 0.0001f)
                    {
                        float dist = math.sqrt(distSq);
                        float weight = 1f - math.saturate(dist / skill.Radius);
                        float2 dir = diff / dist;
                        acceleration.xz += dir * (skill.PullForce * weight);
                        acceleration.y += skill.VerticalForce * weight;
                        health -= skill.Damage;
                    }
                }
                else if (skill.Type == 3)
                {
                    float2 pToS = pos.xz - skill.Position;
                    float dot = math.dot(pToS, skill.Direction);
                    if (dot > 0f && dot < skill.Length)
                    {
                        float2 proj = skill.Position + skill.Direction * dot;
                        float distSq = math.lengthsq(pos.xz - proj);
                        if (distSq < skill.Radius * skill.Radius)
                        {
                            float weight = 1f - math.saturate(math.sqrt(distSq) / skill.Radius);
                            health -= skill.Damage * DeltaTime;
                            acceleration.xz += skill.Direction * (skill.PullForce * weight);
                            acceleration.y += skill.VerticalForce * weight;
                        }
                    }
                }
            }");
        
        File.WriteAllText(path, text);
    }
}
