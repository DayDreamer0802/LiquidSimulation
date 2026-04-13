$file = 'f:\NewRouge\RedVsBlue\Assets\Rouge\RougeGameManager.cs'
$txt = Get-Content $file -Raw -Encoding UTF8
$start = $txt.IndexOf("private void ProcessTornado")
$end = $txt.IndexOf("private void ProcessBomb")
$newTxt = @"
private void ProcessTornado(ref float3 acceleration, ref float health, ref float flashTimer, float3 pos, RougeSkillArea skill)
        {
            float2 diff = pos.xz - skill.Position;
            float distSq = math.lengthsq(diff);
            float outerR = skill.Radius;
            float innerR = math.max(0f, outerR - 6f);
            
            if (distSq < outerR * outerR && distSq > innerR * innerR && distSq > 0.0001f)
            {
                float dist = math.sqrt(distSq);
                float2 dir = diff / dist;
                float weight = 1f - math.saturate((dist - innerR) / 6f);
                
                acceleration.xz += dir * (skill.PullForce * weight * KnockbackResist);
                acceleration.y += skill.VerticalForce * weight * KnockbackResist;
                
                health -= skill.Damage * 0.05f * DeltaTime;
                flashTimer = 1f;
            }
        }

        
"@
$txt = $txt.Remove($start, $end-$start).Insert($start, $newTxt)
Set-Content $file -Value $txt -Encoding UTF8
