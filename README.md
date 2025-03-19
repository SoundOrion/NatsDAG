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

## 📜 ライセンス
MIT License

