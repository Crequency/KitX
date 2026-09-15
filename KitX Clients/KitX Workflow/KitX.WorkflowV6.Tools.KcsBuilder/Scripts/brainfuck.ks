const {
    int memorySize = 30000
    string bfCode = "++++++++[>++++[>++>+++>+++>+<<<<-]>+>+>->>+[<]<-]>>.>---.+++++++..+++.>>.<-.<.+++.------.--------.>>+.>++."
}

var {
    string memory
    int pointer
    int ip
    string inputBuffer
    string outputBuffer
    int inputIndex
    int currentCharCode
    int codeLen
    int tmpInt
    char tmpChar
    bool tmpBool
}

memorySize > CreateMemory > memory
0 > pointer
0 > ip
0 > inputIndex
"" > outputBuffer

bfCode > Len > codeLen
while ip, codeLen > Compare("BLT", _, _):
    bfCode, ip > CharCodeAt > currentCharCode
    // BF instruction dispatch via value-match switch (v6 switch: case label = ASCII code)
    switch currentCharCode:
        43:
            // '+': memory[pointer] = (memory[pointer] + 1) % 256
            memory, pointer > CharCodeAt > tmpInt
            tmpInt, 1, 256 > ModAdd > tmpInt
            tmpInt > Int2Char > tmpChar
            memory, pointer, tmpChar > StringSetChar > memory
        45:
            // '-': memory[pointer] = (memory[pointer] - 1) % 256
            memory, pointer > CharCodeAt > tmpInt
            tmpInt, 1, 256 > ModSub > tmpInt
            tmpInt > Int2Char > tmpChar
            memory, pointer, tmpChar > StringSetChar > memory
        62:
            // '>': pointer++
            pointer, 1 > Add > pointer
        60:
            // '<': pointer--
            pointer, 1 > Sub > pointer
        46:
            // '.': output += char(memory[pointer])
            memory, pointer > CharCodeAt > tmpInt
            tmpInt > Int2Char > tmpChar
            outputBuffer, tmpChar > StringAppendChar > outputBuffer
        44:
            // ',': read input
            inputBuffer > Len > tmpInt
            inputIndex, tmpInt > Compare("BLT", _, _) > tmpBool
            if tmpBool:
                inputBuffer, inputIndex > CharAt > tmpChar
                memory, pointer, tmpChar > StringSetChar > memory
                inputIndex, 1 > Add > inputIndex
        91:
            // '[': if memory[pointer]==0 jump forward
            memory, pointer > CharCodeAt > tmpInt
            tmpInt, 0 > Compare("BEQ", _, _) > tmpBool
            if tmpBool:
                bfCode, ip > FindMatchingForward > ip
        93:
            // ']': if memory[pointer]!=0 jump backward
            memory, pointer > CharCodeAt > tmpInt
            tmpInt, 0 > Compare("BNE", _, _) > tmpBool
            if tmpBool:
                bfCode, ip > FindMatchingBackward > ip
        default:
            // ignore other characters
            0 > tmpInt
    ip, 1 > Add > ip

outputBuffer > Print
Print("Brainfuck program finished")
