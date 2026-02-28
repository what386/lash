# Lash Usage Guide

This guide is a practical walkthrough of the language in small steps.
For runnable full programs, see `examples/`.

## 1. Hello World

```lash
#!/bin/env -S lash run

echo "Hello, Lash!"
```

Run it:

```bash
lash run hello.lash
```

## 2. Variables and Constants

```lash
let name = "Lash"
const version = "0.1"

echo $"{name} {version}"
```

- `let` is mutable.
- `const` is immutable.

## 3. Arithmetic

```lash
let a = 7
let b = 3

let sum = a + b
let product = a * b
let remainder = a % b

echo "sum:" $sum
echo "product:" $product
echo "remainder:" $remainder
```

## 4. Conditionals

```lash
let score = 82

if score >= 90
    echo "A"
elif score >= 80
    echo "B"
else
    echo "C or below"
end
```

## 5. Arrays and Loops

```lash
let items = ["alpha", "beta", "gamma"]

let i = 0
while i < #items
    echo $"{i}: {items[i]}"
    i = i + 1
end

until i == 0
    i = i - 1
end

for item in items
    echo "item:" $item
end
```

## 6. Functions

```lash
fn square(n)
    return n * n
end

let value = 12
echo "square:" square(value)
```

Functions support default parameters:

```lash
fn greet(name, prefix = "hello")
    return $"{prefix}, {name}"
end
```

## 7. Enums and Switch

```lash
enum Mode
    Dev
    Release
end

let mode = Mode::Dev

switch mode
    case Mode::Dev:
        echo "debug settings"
    case Mode::Release:
        echo "optimized settings"
end
```

## 8. Shell Integration

Run shell commands directly as statements:

```lash
pwd
ls -1
```

Capture shell output into a Lash value:

```lash
let branch = $(git rev-parse --abbrev-ref HEAD)
echo "branch:" $branch

fn feed()
    cat
end

feed() << [[line1
line2]]
```

## 9. Subshells and Wait

```lash
subshell into pid
    sh "sleep 1"
end &

wait $pid into status
echo "exit:" $status
```

## 10. Preprocessor Directives

```lash
@define SHOW_MESSAGE true

@if defined(SHOW_MESSAGE) && SHOW_MESSAGE == "true"
echo "compiled with message enabled"
@end
```

You can also import text at compile time:

```lash
@import "notes.txt" into const notes
echo $notes
```

## 11. Running, Checking, Compiling

Run a Lash file:

```bash
lash run script.lash
```

Check semantics only:

```bash
lash check script.lash
```

Compile to Bash:

```bash
lash compile script.lash -o script.sh
bash script.sh
```
