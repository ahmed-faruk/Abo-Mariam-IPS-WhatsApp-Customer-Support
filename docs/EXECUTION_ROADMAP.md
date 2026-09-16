# Abo Mariam IPS WhatsApp Customer Support
## Execution Roadmap — 16 Fixed Stages

> **قاعدة ثابتة:** هذا الملف هو خريطة التنفيذ العليا للمشروع.  
> عندنا **16 مرحلة رئيسية فقط**، ونلتزم بها بالترتيب.  
> GitHub Issues هي تقسيم تنفيذي داخل هذه المراحل، وليست مراحل إضافية.

---

# Source of Truth

المشروع يعتمد على الملفين:

```text
docs/PLAN.md
docs/TECHNICAL.md
```

- `PLAN.md` = ماذا نبني.
- `TECHNICAL.md` = كيف نبنيه.
- لا Codex ولا DeepSeek يغيران الـscope أو الـarchitecture من تلقاء نفسهما.
- أي Feature مؤجل في الـPlan يظل مؤجلًا ما لم يتم اتخاذ قرار صريح بتغيير الـscope.

---

# Current Project Status

تم حتى الآن:

- Git repository جاهز.
- `main` موجود.
- GitHub repository مربوط بالمشروع.
- GitHub CLI authenticated بالحساب `ahmed-faruk`.
- `AGENTS.md` موجود.
- Matt Pocock Skills تم إعدادها.
- Issue Tracker = GitHub Issues.
- Domain layout = single-context.
- ملفات:
  - `docs/PLAN.md`
  - `docs/TECHNICAL.md`
  موجودة في المشروع.
- تم إنشاء GitHub Issues الخاصة بخطة الـMVP.

## مهم

تم إنشاء **19 GitHub Issues** لأن بعض المراحل الكبيرة تم تقسيمها إلى Tasks أصغر.

هذا لا يغير خريطة المشروع:

```text
16 Main Stages
    ↓
GitHub Issues
    ↓
Branches / PRs
```

---

# Stage 1 — تجهيز الـIntel Mac

الهدف: التأكد أن جهاز التطوير مناسب وتشغيل الأدوات الأساسية.

## Verify

```bash
uname -m
sw_vers -productVersion
```

المطلوب:

```text
x86_64
macOS 14 أو أحدث
```

## Xcode Command Line Tools

```bash
xcode-select -p
```

لو غير موجود:

```bash
xcode-select --install
```

## Homebrew

```bash
brew --version
```

ثم:

```bash
brew update
brew install node@24
brew install gh
brew install cloudflared
```

تحقق:

```bash
node --version
npm --version
gh --version
cloudflared --version
git --version
```

## .NET 10 SDK x64

تحقق:

```bash
dotnet --version
dotnet --info
```

المطلوب:

```text
.NET 10.x
Architecture: x64
```

## Docker Desktop Intel

تحقق:

```bash
docker --version
docker compose version
docker run --rm hello-world
```

يفضل في البداية تخصيص حوالي 3–4 GB RAM لـDocker Desktop.

## Ollama Native

```bash
ollama --version
```

> لا نثبت موديل المشروع النهائي قبل مرحلة الـbenchmark.

---

# Stage 2 — Codex + ChatGPT Account

الهدف: تجهيز Codex ليكون مسؤول التفكير والتخطيط والاستلام.

تشغيل:

```bash
codex
```

ثم Login باستخدام ChatGPT.

اختبار:

```text
Reply exactly:
CODEX_OK
```

## مسؤولية Codex

```text
UNDERSTAND
PLAN
ARCHITECT
REVIEW
ACCEPT
```

Codex ليس هو المنفذ الأساسي للكود في الـworkflow الخاص بنا.

---

# Stage 3 — GitHub

الهدف: جعل GitHub هو Source Control + Issues + PRs + CI.

تحقق:

```bash
gh auth status
git remote -v
git branch --show-current
```

الـbranch الأساسي:

```text
main
```

قاعدة المشروع:

```text
1 Coding Ticket
=
1 Branch
=
1 PR
```

ممنوع تطوير Business Code مباشرة على `main`.

---

# Stage 4 — Codex Router + DeepSeek V4.1 Flash

الهدف: Codex يفكر، وDeepSeek ينفذ.

الموديل المستخدم للتنفيذ:

```text
deepseek/deepseek-v4.1-flash
```

تحقق:

```bash
codex-router doctor
codex-router providers
```

Live test:

```bash
codex-router test-model 'deepseek/deepseek-v4.1-flash' --live --yes
```

## تقسيم الأدوار

```text
Codex
  ↓
Plan

DeepSeek V4.1 Flash
  ↓
Implement + Test

Codex
  ↓
Review / Acceptance
```

---

# Stage 5 — Matt Pocock Skills

الهدف: توحيد طريقة التخطيط، TDD، المراجعة وتشخيص المشاكل.

المهارات الأساسية:

```text
setup-matt-pocock-skills
to-tickets
tdd
code-review
diagnosing-bugs
```

الإعداد الحالي:

```text
Issue Tracker = GitHub
Domain Layout = single-context
Default Triage Labels = enabled
```

الملفات الحالية:

```text
AGENTS.md

docs/agents/
├── issue-tracker.md
├── triage-labels.md
└── domain.md
```

---

# Stage 6 — Qodo

الهدف: Independent PR Review بعد مراجعة Codex المحلية.

Qodo يعمل على GitHub PR.

القاعدة:

```text
DeepSeek writes code
        ↓
Codex reviews locally
        ↓
PR
        ↓
Qodo independent review
```

لو Qodo وجد Bug:

```text
Qodo Finding
    ↓
DeepSeek Fix
    ↓
Tests
    ↓
Codex Verify
    ↓
Commit + Push
    ↓
Qodo Reviews Updated PR
```

نفس الـBranch ونفس الـPR.

---

# Stage 7 — Meta / WhatsApp Account

الهدف: تجهيز حسابات WhatsApp Cloud API مبكرًا، بدون ربطها بالكود قبل وقتها.

نحتاج لاحقًا:

```text
Meta Business Portfolio
WhatsApp Business Account (WABA)
Test / Business Phone Number
Phone Number ID
Access Token
Webhook Verify Token
```

## Security

ممنوع Commit لأي Secret.

استخدم لاحقًا:

```text
dotnet user-secrets
environment variables
```

لا نعمل Webhook integration قبل Ticket الخاص بـMeta Adapters.

---

# Stage 8 — تثبيت PLAN و TECHNICAL داخل المشروع

الشكل النهائي:

```text
project/
├── AGENTS.md
├── .agents/
└── docs/
    ├── PLAN.md
    ├── TECHNICAL.md
    └── agents/
```

القواعد:

```text
PLAN.md      = WHAT
TECHNICAL.md = HOW
```

أي Agent يجب أن يقرأهما قبل التخطيط أو التنفيذ.

---

# Stage 9 — إنشاء GitHub Tickets

Codex يقرأ:

```text
AGENTS.md
docs/PLAN.md
docs/TECHNICAL.md
docs/agents/*
```

ثم يحول Delivery Sequence إلى GitHub Issues.

## الوضع الحالي

تم إنشاء Issues من:

```text
#1 → #19
```

وهي تمثل Tasks تنفيذية داخل الـ16 مراحل.

### أبرز الـCoding Issues

```text
#2  Solution + Module Boundaries + Architecture.Tests
#3  GitHub Actions CI
#4  PostgreSQL Docker + EF Migrations
#5  Messaging Inbox/Outbox
#6  Catalog
#7  Storefront
#10 Intelligence / NLU
#11 Conversations
#12 Deterministic Renderer
#13 Meta Adapters
#14 Admin UI
#15 E2E
```

### Human / Setup / Demo Issues

```text
#1  Mac prerequisites
#8  AI Benchmark
#9  Freeze AI model
#16 Cloudflare Tunnel
#17 Live WhatsApp Smoke
#18 AI Pre-warm
#19 Client Demo Readiness
```

> لا ننشئ 24/7 Pilot الآن. هذا يأتي فقط بعد قبول العميل للـPoC.

---

# Stage 10 — تنفيذ كل Coding Ticket

هذه هي دورة العمل الثابتة لكل Ticket.

## 10.1 Update Main

```bash
git switch main
git pull --ff-only
```

## 10.2 Create Branch

مثال:

```bash
git switch -c feat/2-solution-architecture
```

## 10.3 Codex Plans

Codex Native يقرأ:

```text
PLAN.md
TECHNICAL.md
Current GitHub Issue
Current codebase
```

ويعمل:

```text
Exact scope
Architecture constraints
Implementation plan
Tests
Acceptance criteria
Out-of-scope
```

ممنوع Codex يبدأ الـTicket التالي.

## 10.4 DeepSeek Implements

اختيار:

```text
DeepSeek V4.1 Flash
Reasoning = high
```

استخدم Matt TDD skill.

DeepSeek مسؤول عن:

```text
Implement
Test
Fix implementation failures
```

ممنوع:

```text
push
merge
start next ticket
redesign architecture
```

## 10.5 Local Commit

بعد نجاح Build/Tests:

```bash
git status
git diff
git add .
git commit -m "feat: implement <ticket>"
```

## 10.6 Codex Acceptance Review

ارجع Codex Native.

شغل:

```text
code-review
```

ضد `main`.

Codex يراجع:

```text
Ticket fidelity
PLAN fidelity
TECHNICAL fidelity
Architecture
Tests
Regression risks
Security where applicable
Scope creep
```

لو فيه Bug:

```text
Codex Finding
    ↓
DeepSeek Fix
    ↓
Tests
    ↓
Commit
    ↓
Codex Re-review
```

الحد الطبيعي:

```text
Max 2 local fix/review cycles
```

لو مازال Blocker بعد ذلك: نوقف ونحل السبب بدل Loop مفتوح.

---

# Stage 11 — Push + PR + Qodo

بعد Codex PASS:

```bash
git push -u origin <branch-name>
```

ثم:

```bash
gh pr create --base main
```

## PR Gates

```text
GitHub Actions
+
Qodo Review
```

Qodo يراجع الـPR بشكل مستقل.

لو وجد Findings:

```text
DeepSeek Fix
→ Tests
→ Codex Verify
→ Commit
→ Push
→ Qodo Re-review
```

ممنوع Branch جديد لنفس الـTicket.

---

# Stage 12 — Merge

لا Merge إلا عندما:

```text
✓ Ticket scope complete
✓ Required tests green
✓ Codex review clean
✓ GitHub Actions green
✓ Qodo blocking findings resolved
✓ PR conversations resolved
```

ثم:

```text
Squash Merge → main
```

بعدها:

```bash
git switch main
git pull --ff-only
```

ثم نبدأ الـTicket التالي فقط.

---

# Stage 13 — تجهيز Demo العميل

بعد اكتمال الـBusiness Coding والـCI.

نشغل:

```text
PostgreSQL → Docker Desktop
ASP.NET Core → Native
Ollama → Native
cloudflared → Native
```

Public ingress فقط إلى ASP.NET Core.

ممنوع تعريض:

```text
5432  PostgreSQL
11434 Ollama
```

تشغيل Quick Tunnel:

```bash
cloudflared tunnel --url http://127.0.0.1:5000
```

ثم وضع HTTPS URL الناتج في Meta Webhook.

---

# Stage 14 — تجهيز بيانات الـDemo

جهز تقريبًا:

```text
20–40 Product / Variant rows
```

مثال:

```text
Dell P2419H
Dell P2422H
HP E243
Lenovo T24i
```

مع:

```text
Grade A/B/C
Price
Quantity
Warranty
Ports
Specs
Search Tags
```

Business Info:

```text
Working Hours
Address
Delivery
Payment Methods
Warranty Policy
Contact Phone
Return Policy
```

الهدف أن كل Facts في العرض تأتي من PostgreSQL، وليس من الـLLM.

---

# Stage 15 — AI Benchmark + Warm-up

الموديل الأول المرشح للـdemo:

```text
qwen3.5:2b-q4_K_M
```

لكن لا يتم Freeze قبل Benchmark على الـIntel Mac.

## Benchmark

على الأقل 50 حالة مصرية واقعية تغطي:

```text
brand aliases
Arabic-Indic numbers
hard/soft budget
model codes
ports/specs
use cases
follow-up references
business FAQ
human handoff
out-of-scope
prompt injection
```

## Demo Targets

```text
Intent accuracy >= 90%
Hard-budget dedicated cases = 100%
Structured schema success >= 98% after one retry
Warm median <= 8 seconds
Warm p95 <= 12 seconds
```

## Pre-warm before client demo

```text
1. Start Ollama
2. Call selected model once
3. Send representative second request
4. Confirm warm latency
5. Keep model loaded during demo
```

---

# Stage 16 — Client Demo

قبل العرض:

```text
✓ Full CI green
✓ Qodo reviews clean
✓ PostgreSQL migrations work
✓ Real WhatsApp inbound/outbound works
✓ Duplicate inbound → one response
✓ AI benchmark passed
✓ Tunnel works
✓ Model warmed
✓ Smoke run succeeds twice
```

## Demo Script

نفذ بالترتيب:

```text
1. "عندك ديل 24؟"

2. "عايز IPS وفي HDMI"

3. "في حدود 3000"

4. "مش عايز أعدي 2500"

5. "سعر الأولى كام؟"

6. غير السعر من Admin
   ثم اسأل مرة أخرى
   → يجب أن يظهر السعر الجديد

7. اجعل Quantity = 0
   ثم اسأل مرة أخرى
   → يجب ألا يظهر المنتج كمتاح

8. "مواعيدكم إيه؟"

9. غير المواعيد من Admin
   ثم اسأل مرة أخرى
   → يجب أن تظهر القيمة الجديدة

10. "عايز أكلم حد"
    → Conversation switches to Human mode

11. افتح Conversation من Admin
```

## ما يثبته العرض

```text
AI understands Egyptian Arabic
Data is live
Price/stock/specs are deterministic
No commercial hallucination
Admin changes are reflected immediately
Human takeover works
```

---

# Fixed End-to-End Workflow

```text
SETUP ONCE
──────────
Mac
↓
Codex
↓
GitHub
↓
Codex Router + DeepSeek
↓
Matt Skills
↓
Qodo
↓
Meta


FOR EVERY CODING TICKET
───────────────────────
Codex PLAN
↓
DeepSeek IMPLEMENT + TEST
↓
Commit
↓
Codex REVIEW
↓
Push
↓
PR
↓
GitHub Actions + Qodo
↓
DeepSeek FIX
↓
Codex VERIFY
↓
Qodo CLEAN
↓
Squash Merge
↓
main
↓
NEXT TICKET


CLIENT DEMO
───────────
PostgreSQL
+
ASP.NET Core
+
Ollama
+
Cloudflare
+
Meta WhatsApp
↓
Smoke Test ×2
↓
Warm AI
↓
CLIENT DEMO
```

---

# Golden Rules

1. عندنا **16 مرحلة رئيسية فقط**.
2. لا نبدأ مرحلة لاحقة قبل إتمام prerequisites الخاصة بها.
3. `PLAN.md` و`TECHNICAL.md` هما Source of Truth.
4. Codex = Think / Plan / Review / Accept.
5. DeepSeek V4.1 Flash = Implement / Test / Fix.
6. Matt Skills = Workflow discipline.
7. GitHub Actions = Automated hard quality gate.
8. Qodo = Independent PR review.
9. Coding Ticket واحد = Branch واحد = PR واحد.
10. Qodo fixes على نفس الـBranch والـPR.
11. لا Merge مع Tests أو CI أو Qodo blockers.
12. Squash Merge إلى `main`.
13. لا نبدأ Next Ticket قبل Merge الحالي.
14. لا Secrets في Git.
15. AI لا يملك السعر أو الـstock أو المواصفات التجارية.
16. 24/7 Pilot يبدأ فقط بعد قبول العميل للـPoC.

---

# Current Next Action

الوضع الحالي وصل إلى:

```text
Stage 9 — GitHub Tickets created
```

قبل بدء Business Coding:

```text
1. تأكد من Stage 6 — Qodo مرتبط بالRepository.
2. تأكد من Stage 7 — Meta account/setup الأساسي جاهز.
3. أغلق/أكد Issue #1 الخاص بتجهيز الماك إذا كل prerequisites ناجحة.
4. ابدأ Issue #2:
   MVP 1: Create .NET solution, modular boundaries, and Architecture.Tests
```

بعدها تبدأ أول دورة Coding رسمية:

```text
Issue #2
↓
Codex Plan
↓
Branch
↓
DeepSeek Implement + Test
↓
Codex Review
↓
PR
↓
Actions + Qodo
↓
Fix
↓
Merge
```
