# MANIFEST — 14b_P6_G1G2_checkpoint (checkpoint MI-PHASE, hors schéma de slots)

**Rôle :** checkpoint de P6 **EN COURS** — G1 (double-sided) + G2 (absorption immergée) livrés/validés,
G3 (Snell) pas encore commencé. **PAS** le snapshot de fin de phase (celui-ci sera `15_P6_..._validated/`).
**Rollback de reprise si besoin ; le rollback de sécurité amont reste `14_before_P6_underwater/`.**

## État capturé (92 fichiers + HASHES.sha256)
- `OceanSurface.shader` : **Cull Off** sur GBuffer/DepthOnly/MotionVectors (double-sided, G1). ShadowCaster/Picking = Cull Back.
- `Shaders/OceanUnderwater.shader` : fullscreen CustomPass `BeforePostProcess`, `color*=exp(−σ·d)` (σ=`_WaterAbsorption` partagé), gaté `_OceanUnderwaterEnabled`.
- `OceanUnderwaterModule.cs` : réel (CustomPassVolume+FullScreenCustomPass, gate immersion, push non destructif ; param `underwaterDensity`).
- `OceanReflectionModule.cs` (P5), `Ocean Debug Sky.volumeprofile` (P5), scène propre (caméra restaurée à (0, 9.02, −10)).

## Validé utilisateur
- **G1** : surface visible de dessous (raccord dessus/dessous, Q4.1). ✅
- **G2** : vue immergée s'assombrit/teinte bleu-vert (absorption partagée surface/sous-marin, Q6.1). ✅

## Reste (P6) : G3 Snell → G4 fog/god-rays → G5 éclairage non destructif + re-valid T1.
Cadrage G3 détaillé : `OCEAN_TEST_P6.md`.
