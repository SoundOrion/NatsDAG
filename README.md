# NATS + C# での DAG (Directed Acyclic Graph) 実装

## 🚀 概要
このプロジェクトは、 **NATS** を使用して **DAG（有向非巡回グラフ）** を C# で実装するものです。

NATS の **Pub/Sub（パブリッシュ/サブスクライブ）メッセージング** を活用し、
各ノードがメッセージを受信・処理し、次のノードへデータを送信する形で DAG を表現しています。

**特徴:**
- **NATS.Net** を使用した最新の .NET 8 向け実装
- **非同期 (async/await) 処理** により高速かつ並列実行可能
- **依存関係を考慮** した DAG ノードの実行制御
- **スレッドセーフな `Channel` を活用** してノードの同期を管理
- **スケール可能な分散処理アーキテクチャの基盤**

## 🛠️ 必要な環境
- **.NET 8 以上**
- **NATS Server**（ローカルまたは Docker で実行可能）

## 📦 インストール & セットアップ
### 1️⃣ **リポジトリをクローン**
```sh
git clone https://github.com/your-repo/nats-dag-csharp.git
cd nats-dag-csharp
```

### 2️⃣ **NATS クライアントライブラリのインストール**
プロジェクトディレクトリで以下のコマンドを実行し、NATS.Net をインストールしてください。
```sh
dotnet add package NATS.Net
```

### 3️⃣ **NATS サーバーの起動**
#### **ローカルで直接実行する場合**
```sh
nats-server
```
#### **Docker を使う場合**
```sh
docker run --rm -p 4222:4222 nats
```

## 🚀 実行方法
```sh
dotnet run
```

## ⚙️ DAG の構造
このプロジェクトでは、以下の DAG 構造を NATS メッセージングで実装しています。
```
     A
    / \
   B   C
    \ /
     D
```
- **A** は最初のノードで、B と C にメッセージを送信。
- **B, C** はそれぞれ処理を行い、D にメッセージを送信。
- **D** は **B と C の両方のメッセージを受け取った後に処理を開始**。

## 🔄 主要なコードの説明
### **DAG ノードのクラス (`DAGNode.cs`)**
各ノードは **NATS のサブスクライバー (Subscriber) & パブリッシャー (Publisher)** になり、
メッセージを処理し、次のノードに送信します。
```csharp
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using NATS.Client.Core;

class DAGNode {
    private readonly string nodeName;
    private readonly List<string> nextNodes;
    private readonly NatsConnection natsConnection;
    private readonly Dictionary<string, int> dependencyCounts;
    private readonly Channel<string> dependencyChannel;

    public DAGNode(string nodeName, List<string> nextNodes, NatsConnection connection, Dictionary<string, int> dependencyCounts = null) {
        this.nodeName = nodeName;
        this.nextNodes = nextNodes;
        this.natsConnection = connection;
        this.dependencyCounts = dependencyCounts;

        if (dependencyCounts != null && dependencyCounts.ContainsKey(nodeName)) {
            dependencyChannel = Channel.CreateUnbounded<string>();
        }
    }

    public async Task StartAsync() {
        await foreach (var msg in natsConnection.SubscribeAsync<byte[]>(nodeName)) {
            string receivedMessage = Encoding.UTF8.GetString(msg.Data);
            Console.WriteLine($"[Node {nodeName}] Received: {receivedMessage}");
            await Task.Delay(500);

            if (dependencyChannel != null) {
                await dependencyChannel.Writer.WriteAsync(receivedMessage);
                if (dependencyChannel.Reader.Count < dependencyCounts[nodeName]) {
                    Console.WriteLine($"[Node {nodeName}] Waiting for {dependencyCounts[nodeName] - dependencyChannel.Reader.Count} more dependencies.");
                    continue;
                }
                for (int i = 0; i < dependencyCounts[nodeName]; i++) {
                    _ = await dependencyChannel.Reader.ReadAsync();
                }
                Console.WriteLine($"[Node {nodeName}] All dependencies met, processing...");
            }

            foreach (var nextNode in nextNodes) {
                var message = $"Processed by {nodeName}";
                await natsConnection.PublishAsync(nextNode, Encoding.UTF8.GetBytes(message));
                Console.WriteLine($"[Node {nodeName}] Sent to {nextNode}: {message}");
            }
        }
    }
}
```

### **メインプログラム (`Program.cs`)**
```csharp
static async Task Main() {
    await using var connection = new NatsConnection(new NatsOpts { Url = "nats://localhost:4222" });
    var dependencyCounts = new Dictionary<string, int> { { "D", 2 } };
    var dagNodes = new Dictionary<string, DAGNode> {
        { "A", new DAGNode("A", new List<string> { "B", "C" }, connection) },
        { "B", new DAGNode("B", new List<string> { "D" }, connection) },
        { "C", new DAGNode("C", new List<string> { "D" }, connection) },
        { "D", new DAGNode("D", new List<string>(), connection, dependencyCounts) }
    };
    var tasks = new List<Task>();
    foreach (var node in dagNodes.Values) {
        tasks.Add(node.StartAsync());
    }
    await connection.PublishAsync("A", Encoding.UTF8.GetBytes("Start DAG"));
    Console.WriteLine("DAG Execution Started. Press Enter to exit.");
    Console.ReadLine();
}
```

## ✅ 確認済みの動作結果
```
DAG Execution Started. Press Enter to exit.
[Node A] Received: Start DAG
[Node A] Sent to B: Processed by A
[Node A] Sent to C: Processed by A
[Node B] Received: Processed by A
[Node C] Received: Processed by A
[Node B] Sent to D: Processed by B
[Node C] Sent to D: Processed by C
[Node D] Received: Processed by B
[Node D] Waiting for 1 more dependencies.
[Node D] Received: Processed by C
[Node D] All dependencies met, processing...
```

追加できる機能や改善点をいくつか提案します！ 🚀  
現在の DAG システムは **NATS を使った非同期メッセージ処理** を実現できていますが、さらに拡張すれば **柔軟で堅牢な分散処理システム** を構築できます。

---

## **💡 追加できる機能・改善点**
### **1️⃣ DAG の構造を動的に定義できるようにする**
**📌 現状:**  
現在は **C# のコード内で DAG のノードと依存関係を定義** しています。  
**🔧 改善:**  
DAG の構造を **JSON や YAML で定義し、プログラム実行時に読み込んで動的に構築** できるようにすると、**ノードを追加・変更するたびにコードを修正する必要がなくなります。**

**✅ できること:**
- DAG の定義を外部ファイル (`dag.json`) から読み込み
- JSON を解析し、DAG のノードと依存関係を動的に作成
- DAG の再利用や変更が容易に

**📝 例: `dag.json`**
```json
{
  "nodes": ["A", "B", "C", "D", "E"],
  "edges": {
    "A": ["B", "C"],
    "B": ["D"],
    "C": ["D"],
    "D": ["E"]
  },
  "dependencies": {
    "D": 2,
    "E": 1
  }
}
```

---

### **2️⃣ メッセージの永続化と再送機能を追加（NATS JetStream）**
**📌 現状:**  
NATS は **リアルタイムでメッセージを送信** するため、ノードがダウンしているとメッセージを受け取れずに消失する可能性があります。  
**🔧 改善:**  
**NATS JetStream** を導入すると、メッセージを **永続化（保存）** でき、ノードが復帰後にメッセージを再送できるようになります。

**✅ できること:**
- **メッセージの永続化**（障害が発生してもデータを保持）
- **遅延処理**（ノードが遅れてもメッセージを受信可能）
- **リプレイ機能**（過去のメッセージを再処理）

**📝 JetStream を有効にするコード例**
```csharp
var js = new NatsJsContext(natsConnection);
await js.PublishAsync("D", Encoding.UTF8.GetBytes("Processed by B"));
```

---

### **3️⃣ エラーハンドリング & リトライ機能**
**📌 現状:**  
現在のコードでは **ノードの処理が失敗した場合、リトライせずに終了** してしまいます。  
**🔧 改善:**  
**エラーハンドリング & 自動リトライ機能** を追加することで、**一時的な障害にも対応できる DAG を構築可能** です。

**✅ できること:**
- **ノードの処理失敗時にリトライ（NATSのメッセージ再送）**
- **リトライ回数の上限を設定（例: 最大 3 回）**
- **リトライ間隔を増やす（指数バックオフ）**

**📝 エラーハンドリングの追加例**
```csharp
foreach (var nextNode in nextNodes)
{
    var message = $"Processed by {nodeName}";
    int retryCount = 0;
    bool success = false;

    while (retryCount < 3 && !success)
    {
        try
        {
            await natsConnection.PublishAsync(nextNode, Encoding.UTF8.GetBytes(message));
            success = true;
        }
        catch (Exception ex)
        {
            retryCount++;
            Console.WriteLine($"[Error] Failed to send message to {nextNode}. Retrying {retryCount}/3...");
            await Task.Delay(1000 * retryCount); // 指数バックオフ
        }
    }
}
```

---

### **4️⃣ ノードの並列処理最適化**
**📌 現状:**  
現在のノードの処理は **逐次的** に実行され、**ノードの数が増えると処理時間が長くなる可能性があります。**  
**🔧 改善:**  
ノードを **複数のインスタンスで並列実行** できるようにすると、スケールアウト可能になります。

**✅ できること:**
- **複数のプロセスで同じノードを並列処理（例: D のインスタンスを 2 つに増やす）**
- **負荷分散を行い、処理速度を向上**
- **CPU / メモリを効率的に使用**

**📝 並列実行の設定（ワーカーを増やす）**
```csharp
var subscription = natsConnection.SubscribeAsync<byte[]>(nodeName);
for (int i = 0; i < Environment.ProcessorCount; i++)
{
    _ = Task.Run(async () =>
    {
        await foreach (var msg in subscription)
        {
            await ProcessMessage(msg);
        }
    });
}
```

---

### **5️⃣ Web API を追加して DAG の状態を確認**
**📌 現状:**  
DAG の実行状況を **コンソールで確認する** しかなく、外部システムとの連携ができません。  
**🔧 改善:**  
**Web API を作成** し、DAG の現在の状態をリアルタイムで確認できるようにします。

**✅ できること:**
- **DAG のノード状態を Web API で取得**
- **API 経由で DAG の実行をトリガー**
- **DAG の状態を可視化（フロントエンドと連携）**

**📝 Web API の追加（ASP.NET Core 使用）**
```csharp
app.MapGet("/dag/status", () =>
{
    return Results.Json(new { nodes = dagNodes.Keys, status = "running" });
});
```

---

## **🔚 まとめ**
| #  | 改善点 | 内容 |
|----|--------|------|
| 1️⃣ | **DAG の動的構築** | JSON / YAML で DAG を定義し、コードを修正せずに変更可能 |
| 2️⃣ | **NATS JetStream** | メッセージの永続化 & リプレイ機能で信頼性向上 |
| 3️⃣ | **エラーハンドリング & リトライ** | 失敗時の自動リトライ & 指数バックオフ |
| 4️⃣ | **ノードの並列処理** | スケールアウトして高速処理が可能 |
| 5️⃣ | **Web API の追加** | DAG の実行状況を API で取得し、可視化 |

---

### **🛠 次のステップ**
どの機能を追加したいですか？  
**「すぐ実装したい機能」** や **「優先度の高いもの」** を教えてくれれば、それに合わせて実装方法を具体化できます！ 🚀