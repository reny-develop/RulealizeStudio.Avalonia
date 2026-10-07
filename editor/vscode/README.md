# RulealizeStudio.Avalonia

Make an Avalonia application without reading JSON or XAML. You design its screen and write what it
should do; an agent you already use writes the rules from them; and you read on the application's
own window everything those rules allow — every situation it can be brought to, the steps that
reach it, what can be done there and what is refused — asking for changes until it is what you
meant.

## What you do

- **Design the screen** on the application's own window: drag controls onto it, move them, size
  them, give them words and colours.
- **Write what it should do** as a diagram of states and what the person does between them, read
  back as sentences.
- **Ask your agent for the rules**, in whatever app you use it in. Under the screen and the
  specification, the view says anything the agent changed of what you made.
- **Read its test cases**, each the steps to take on the window and what the window should be
  after them, every refusal with the sentence it is said in.
- **Ask for a change**, and read what it did: the test cases it changed, added or removed, each
  before and after, on the window.

Keeping what you made is a commit in VS Code's Source Control.

## Where to begin

Open an empty folder, then **RulealizeStudio.Avalonia** in the activity bar: **New application**
makes one, and **Start from an example** makes one that already has its rules. The walkthrough on
VS Code's welcome page goes through the example, and
[Building an application this way](https://github.com/reny-develop/RulealizeStudio.Avalonia/blob/main/doc/practice.md)
takes you from an empty folder to an application of your own in about half an hour.

The first time, the extension asks before fetching what it needs: a .NET runtime, the .NET SDK 10.0
where an application is built, and `ruledger`. It speaks English and Japanese, whichever VS Code is
set to.

## More

How it works, and how it is built:
[github.com/reny-develop/RulealizeStudio.Avalonia](https://github.com/reny-develop/RulealizeStudio.Avalonia).
The method it puts into practice:
[Rule-Derived Test Design](https://github.com/reny-develop/rule-derived-test-design).
