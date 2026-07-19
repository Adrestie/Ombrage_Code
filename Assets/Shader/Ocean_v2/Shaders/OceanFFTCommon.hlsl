// ============================================================================
//  OceanFFTCommon.hlsl  (Ocean_v2)
//  Bibliothèque HLSL commune à la simulation FFT : arithmétique complexe,
//  symétrie hermitienne, et helpers de PACKING « 2-en-1 ».
//
//  Réécriture FROM SCRATCH (l'ancien océan n'a PAS d'équivalent : son cœur
//  complexe vivait inline dans OceanFFT.compute, sans packing hermitien).
//
//  --- Astuce hermitienne 2-en-1 ---------------------------------------------
//  Une IFFT est LINÉAIRE :  IFFT(A + i·B) = IFFT(A) + i·IFFT(B).
//  Si A(k) et B(k) sont deux spectres HERMITIENS (A(-k)=conj(A(k))), alors
//  IFFT(A) et IFFT(B) sont tous deux RÉELS. Donc en empaquetant
//        C(k) = A(k) + i·B(k)         (Pack2)
//  une SEULE IFFT complexe donne, dans son résultat complexe :
//        partie réelle  = signal A
//        partie imaginaire = signal B
//  => 2 signaux réels pour 1 IFFT (gain ~50 % vs 2 IFFT séparées).
//
//  Tous les coefficients spectraux de l'océan sont hermitiens :
//   - hauteur          h(k,t)         avec h(-k)=conj(h(k))  (surface réelle)
//   - déplacement      -i·(k/|k|)·h(k)
//   - pente/normale     i·k·h(k)
//   - Jacobien          -(k⊗k/|k|)·h(k)
//  (vérifié : f(k) hermitien  =>  i·k·f(k) hermitien, etc.)
// ============================================================================

#ifndef OCEAN_FFT_COMMON_INCLUDED
#define OCEAN_FFT_COMMON_INCLUDED

#define OCEAN_PI      3.14159265358979323846
#define OCEAN_TWO_PI  6.28318530717958647692
#define OCEAN_G       9.80665          // accélération de pesanteur (m/s²)
#define OCEAN_INV_SQRT2 0.70710678118  // 1/sqrt(2)

// ── Arithmétique complexe (float2 = (re, im)) ───────────────────────────────
float2 ocean_cadd(float2 a, float2 b) { return a + b; }

float2 ocean_cmul(float2 a, float2 b)
{
    // (ar + i ai)(br + i bi)
    return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

// Multiplication par i :  i·(re + i·im) = -im + i·re
float2 ocean_cmuli(float2 a) { return float2(-a.y, a.x); }

// Conjugué : conj(re + i·im) = re - i·im
float2 ocean_conj(float2 a) { return float2(a.x, -a.y); }

// e^{i·angle}
float2 ocean_cexp(float angle) { return float2(cos(angle), sin(angle)); }

// ── Packing « 2-en-1 » ──────────────────────────────────────────────────────
// Empaquette deux spectres hermitiens A,B en un seul spectre complexe C = A + i·B.
// (À envoyer tel quel dans l'IFFT générique.)
float2 OceanPack2(float2 specA, float2 specB)
{
    return specA + ocean_cmuli(specB);   // A + i·B
}

// Après l'IFFT du paquet : .x = signal A (réel), .y = signal B (réel).
// (Aucune extraction « parties paire/impaire » nécessaire ici : la linéarité de
//  l'IFFT sur des spectres hermitiens suffit — cf. en-tête.)
float OceanUnpackA(float2 ifftResult) { return ifftResult.x; }
float OceanUnpackB(float2 ifftResult) { return ifftResult.y; }

// ── Indexation fréquentielle (DC au COIN, convention standard DFT) ───────────
// Le texel id ∈ [0,N) porte la fréquence signée n = (id<N/2) ? id : id-N.
// => DC à id=0, fréquences négatives dans la moitié haute. Aucune passe de
//    « fftshift » (-1)^(x+y) n'est alors nécessaire (l'ancien océan en avait une
//    car il centrait le DC en N/2 ; ici on évite cette passe).
int2 OceanSignedFreqIndex(uint2 id, uint N)
{
    int2 n;
    n.x = (id.x < (N >> 1)) ? (int)id.x : (int)id.x - (int)N;
    n.y = (id.y < (N >> 1)) ? (int)id.y : (int)id.y - (int)N;
    return n;
}

// Vecteur d'onde k = 2π·n / L  (Δk = 2π/L dépend de L SEULEMENT, jamais de N :
// c'est la clé du découplage amplitude↔résolution, cf. anti-bug n°3).
float2 OceanWaveVector(int2 n, float L)
{
    return (OCEAN_TWO_PI / L) * float2(n);
}

// ── Dispersion ──────────────────────────────────────────────────────────────
// Deep-water :  ω = sqrt(g·|k|).
// TMA/shallow  :  ω = sqrt(g·|k|·tanh(|k|·h)).  En pleine mer, h≈191 m => tanh→1 => deep.
float OceanDispersion(float kMag, float depth, bool useTMA)
{
    if (kMag < 1e-8) return 0.0;
    float gk = OCEAN_G * kMag;
    if (useTMA)
        return sqrt(gk * tanh(min(kMag * depth, 20.0)));   // clamp pour éviter overflow
    return sqrt(gk);
}

// dω/dk (utile pour la conversion S(ω)→Ψ(k)). Deep : 0.5·sqrt(g/k).
float OceanDispersionDeriv(float kMag, float depth, bool useTMA)
{
    if (kMag < 1e-8) return 0.0;
    if (useTMA)
    {
        float kh = min(kMag * depth, 20.0);
        float th = tanh(kh);
        float sech2 = 1.0 - th * th;
        float w = sqrt(OCEAN_G * kMag * th);
        // d/dk [ g k tanh(kh) ] = g( tanh(kh) + k h sech²(kh) )
        float dInner = OCEAN_G * (th + kMag * depth * sech2);
        return dInner / (2.0 * max(w, 1e-6));
    }
    return 0.5 * sqrt(OCEAN_G / kMag);
}

#endif // OCEAN_FFT_COMMON_INCLUDED
