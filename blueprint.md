# ShapeRise Arena – Project Blueprint v2.0

## 1. Proje Genel Bakışı
- **Oyun Adı**: ShapeRise Arena
- **Tür**: Rekabetçi, gerçek zamanlı Merge + Tower Building Race
- **Oyuncu Sayısı**: 2 veya 4 oyuncu (Free-for-All veya Team 2v2 modları)
- **Platformlar**: Mobil (iOS/Android) + PC (Cross-platform)
- **Hedef**: Aynı renk ve aynı şekli birleştirerek kendi "Yükseklik Kulesi"ni ilk seviyedeki hedef çizgisine (örneğin 1000 birim) ulaştıran oyuncu kazanır.
- **Temel Mekanik**: 
  - Düşen şekiller: Üçgen, Kare, Dikdörtgen, Yuvarlak
  - Renkler: Kırmızı, Mavi, Yeşil, Sarı, Mor (5 renk – ileride genişletilebilir)
  - Birleştirme Kuralı: **Sadece aynı şekil + aynı renk** birleşir. Farklı olanlar fizikle çarpışır ve yığılır.
  - Yükselme: Her başarılı birleştirme → Height Bar artışı + görsel kule yükselme efekti (particle, sound, camera shake).
- **Kazanma Şartı**: İlk seviyede hedef yüksekliğe ulaşan kazanır. İleride birden fazla seviye eklenebilir.

## 2. Teknik Mimari (Senior Level)
- **Unity Versiyonu**: Unity 6 LTS (2025/2026) önerilir.
- **Networking**: Mirror Networking + **KCP Transport** (UDP, düşük latency – %30-40 daha hızlı).
- **Sunucu Modeli**: Dedicated Headless Linux Server (Server-authoritative).
- **Deployment**: Docker + docker-compose (kolay scaling, CI/CD uyumlu).
- **Scripting Backend**: İlk aşamada **Mono** (daha küçük Docker imajı ve stabilite), ileride performans kritikse IL2CPP’e geçiş.
- **Physics**: 2D (Rigidbody2D + PolygonCollider2D başlangıçta). Merge için **custom lightweight collision check** önerilir (fizik yükünü azaltmak için).
- **Yetki Dağılımı**:
  - Spawn, merge detection, height calculation, bot logic → **Server**.
  - Input (drop position, rotation, hızlandırma) → Client → ServerRpc.
  - Görsel efektler, UI, kamera → Client-side prediction + reconciliation (gerektiğinde).
- **Anti-Cheat**: Tüm merge ve height işlemleri server’da doğrulanır. Client sadece input gönderir.

## 3. Klasör Yapısı (Root)

```
ShapeRise Arena/
├── docker/
│   ├── Dockerfile              # Ubuntu 22.04, non-root, UDP 7777
│   ├── docker-compose.yml      # Service, volumes, resource limits
│   └── .env.example            # Ortam değişkenleri şablonu
│
├── server-build/               # Unity headless Linux build çıktısı (CI/CD üretir)
│   └── ShapeRiseServer.x86_64
│
├── unity-project/
│   └── Assets/
│       └── Scripts/
│           ├── Core/
│           │   ├── GameEnums.cs        # ShapeType, ShapeColor, GameState, GameMode, PlayerType
│           │   └── GameEvents.cs       # Server-side static event bus
│           │
│           ├── Networking/
│           │   ├── ArenaNetworkManager.cs  # extends NetworkManager; bağlantı + bot fill
│           │   └── GameController.cs       # NetworkBehaviour; SyncVar state, SyncList heights
│           │
│           ├── Gameplay/
│           │   ├── ShapeController.cs      # NetworkBehaviour; SyncVar type/color/merged
│           │   ├── ShapeSpawner.cs         # Server-only; lane bazlı spawn
│           │   ├── MergeDetector.cs        # Server-only; spatial bucket hash merge detection
│           │   └── PlayerInputHandler.cs   # Client-owned; input → CmdDrop
│           │
│           ├── AI/
│           │   └── BotAIController.cs      # Server-only; grid scoring + %17.5 error rate
│           │
│           ├── UI/
│           │   └── HeightBarUI.cs          # SyncList'ten okur, no RPC
│           │
│           └── DeepLink/
│               └── DeepLinkHandler.cs      # shaperise://join/ROOMID parser
│
└── blueprint.md
```

## 4. Data Flow (Server-Authoritative)

```
[Client]  key/touch input
    │
    ▼  CmdDrop(x, rot)           [requiresAuthority=false]
[Server] ShapeController.CmdDrop()
    │  enables Rigidbody2D gravity
    │
    ▼  Update loop
[Server] MergeDetector  →  spatial bucket sweep every 80ms
    │  CanMerge? (same type + color + owner + proximity)
    │
    ▼  ExecuteMerge()
[Server] GameEvents.RaiseHeightDelta(ownerNetId, +50pts)
    │
    ▼
[Server] GameController.HandleHeightDelta()
    │  PlayerHeights[slot] += delta   (SyncList → auto-sync to clients)
    │  if >= 1000 → GameEvents.RaisePlayerWon()
    │
    ▼  RpcOnGameOver(winnerNetId)
[All Clients] show result screen
```

## 5. Mimari Kararlar ve Gerekçeler

| Karar | Seçim | Gerekçe |
|---|---|---|
| Merge detection | Spatial bucket hash | O(n/k) vs O(n²) brute force; fizik callback'i yok |
| Networking | Mirror + KCP Transport | UDP, düşük latency, %30-40 hız avantajı |
| Server authority | NetworkManager + GameController | SyncVar/ClientRpc NetworkBehaviour gerektirir; ayrı prefab |
| Scripting backend | Mono (ilk aşama) | Küçük Docker imajı, stabil, Linux headless uyumlu |
| Bot error rate | %17.5 | 15-20% spec'inin orta noktası |
| Deep link güvenlik | Alphanumeric + hyphen only | Injection saldırılarına karşı input validation |

## 6. Implementation Plan (Mevcut Aşama: Foundation)

### ✅ Tamamlananlar
- [x] Docker altyapısı (Dockerfile + docker-compose + .env.example)
- [x] Core enums + event bus
- [x] ArenaNetworkManager (bağlantı, bot fill, 60s kuralı)
- [x] GameController (SyncVar state, SyncList heights, win condition)
- [x] ShapeController (SyncVar type/color/merged, CmdDrop)
- [x] ShapeSpawner (server-authoritative, lane bazlı)
- [x] MergeDetector (spatial bucket hash, neighbor check)
- [x] PlayerInputHandler (owned, TargetRpc assign, CmdDrop)
- [x] BotAIController (grid scoring, %17.5 error rate)
- [x] HeightBarUI (SyncList okuyucu)
- [x] DeepLinkHandler (shaperise://join/ROOMID)

### 🔜 Sonraki Aşama
- [ ] Unity sahne kurulumu (prefab atama, NetworkManager config)
- [ ] Shape prefabs (4 tiP × SpriteRenderer + PolygonCollider2D)
- [ ] Headless Linux build + Docker imajı test
- [ ] Merge VFX (particle, sound, camera shake)
- [ ] Matchmaking REST API → DeepLink roomId çözümü
- [ ] IL2CPP geçişi (performans aşaması)

## 7. Test Checklist

### Docker / Server
- [ ] `docker-compose up` → container ayağa kalkar
- [ ] `docker logs shaperise-arena` → server started mesajı görülür
- [ ] UDP 7777 dışarıdan erişilebilir
- [ ] Healthcheck pass ediliyor

### Networking (Play Mode, 2 client)
- [ ] 2 client bağlandığında countdown başlar
- [ ] 60s dolduğunda bot inject edilir
- [ ] Geç bağlanan client reddedilir (GameState != WaitingForPlayers)

### Gameplay
- [ ] Shape spawn: doğru lane, doğru tip/renk SyncVar
- [ ] Drop: client input → server hareket → tüm clientlarda görülür
- [ ] Merge: aynı tip+renk+owner → merge tetiklenir, yanlış tip → merge olmaz
- [ ] Height: merge sonrası SyncList güncellenir, UI bar yükselir
- [ ] Win: 1000 puana ulaşan oyuncu için GameOver RPC tüm clientlara gönderilir

### Bot AI
- [ ] 60s sonra bot spawn
- [ ] Bot drop kararı 1.2s gecikmeyle gelir
- [ ] ~10 match sonrası bot win rate %60-70 arasında (error rate test)

### Deep Link
- [ ] `shaperise://join/test-room-123` → NetworkManager.StartClient() çağrılır
- [ ] Geçersiz karakter → reddedilir, uygulama crash etmez