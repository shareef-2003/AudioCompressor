# 🎵 Audio Compressor — ضغط ملفات الصوت

## مشروع مقرر الوسائط المتعددة 2026

---

## المتطلبات

- Windows 10/11
- Visual Studio 2019 أو أحدث (.NET Framework 4.7.2)
- أو VS Code مع .NET Framework SDK

---

## طريقة الفتح في Visual Studio 2019

1. افتح `AudioCompressor.sln`
2. Build > Build Solution (Ctrl+Shift+B)
3. Debug > Start Debugging (F5)

---

## طريقة الفتح في VS Code

```bash
# تأكد من تثبيت .NET SDK
dotnet --version

# من داخل مجلد AudioCompressor/
cd AudioCompressor
dotnet build AudioCompressor.csproj
dotnet run
```

---

## هيكل المشروع

```
AudioCompressor/
├── AudioCompressor.sln
└── AudioCompressor/
    ├── Program.cs                          # نقطة الدخول
    ├── MainForm.cs                         # النافذة الرئيسية (كل المنطق)
    ├── MainForm.Designer.cs                # إعدادات النموذج
    ├── Algorithms/
    │   ├── ICompressionAlgorithm.cs        # الواجهة المشتركة
    │   ├── NonlinearQuantization.cs        # NLQ — قانون mu-law
    │   ├── DPCM.cs                         # Differential PCM
    │   ├── PredictiveDifferentialCoding.cs # PDC — تنبؤ خطي
    │   ├── DeltaModulation.cs              # DM — بت واحد لكل عينة
    │   └── AdaptiveDeltaModulation.cs      # ADM — خطوة متكيفة
    ├── Models/
    │   └── AudioFile.cs                    # AudioFile + Settings + Result
    └── UI/
        ├── WaveformPanel.cs                # رسم شكل الموجة
        └── ChartPanel.cs                   # الرسوم البيانية الحية
```

---

## الميزات المنجزة

| #   | المتطلب                          | الحالة                                |
| --- | -------------------------------- | ------------------------------------- |
| 1   | إدخال الملف (واجهة + سحب وإفلات) | ✅                                    |
| 2   | تشغيل الملف للمعاينة             | ✅ (WAV)                              |
| 3   | عرض خصائص الملف تلقائياً         | ✅                                    |
| 4   | 5 خوارزميات ضغط                  | ✅ NLQ, DPCM, PDC, DM, ADM            |
| 5   | فك الضغط                         | ✅                                    |
| 6   | إعدادات الضغط                    | ✅ Sample Rate, Quant, BitRate, Delta |
| 7   | مراقبة الأداء الزمني الحقيقي     | ✅ شريط تقدم + رسمان بيانيان          |
| 8   | إلغاء العملية                    | ✅                                    |
| 9   | إعادة ضبط                        | ✅                                    |
| 10  | تقرير بعد الضغط                  | ✅                                    |
| 11  | حفظ الملف المضغوط                | ✅ صيغة .acp مخصصة                    |

---

## الخوارزميات

### NLQ — Nonlinear Quantization (μ-law)

تضغط كل عينة 16-bit إلى 8-bit باستخدام دالة logarithmية.
نسبة الضغط: ~50%. يُستخدم في G.711 (هاتف PSTN).

### DPCM — Differential PCM

يُخزّن فرق العينة الحالية عن السابقة (8-bit بدل 16-bit).
نسبة الضغط تعتمد على ترابط الإشارة.

### PDC — Predictive Differential Coding

تنبؤ خطي من 4 عينات سابقة، تخزين الخطأ فقط.
يحقق ضغطاً أعلى من DPCM.

### DM — Delta Modulation

بت واحد لكل عينة (رفع/خفض بخطوة ثابتة).
نسبة ضغط 12.5% من الحجم الأصلي.

### ADM — Adaptive Delta Modulation

مثل DM لكن الخطوة تتكيف مع سرعة الإشارة.
جودة أفضل من DM مع ضغط مماثل.

---

مع التوفيق 🎓
