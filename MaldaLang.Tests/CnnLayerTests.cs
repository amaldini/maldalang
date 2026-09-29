// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Tests;

using Xunit;

public class CnnLayerTests : TestBase
{
    [Fact]
    public void Conv2D_BasicForward_WorksCorrectly()
    {
        const string code = @"
            var conv = new Conv2D(1, 1, 3, 1, ""valid"", 0.0);
            conv.weights[0][0][0][0] = 1.0;
            conv.weights[0][0][0][1] = 0.0;
            conv.weights[0][0][0][2] = 0.0;
            conv.weights[0][0][1][0] = 0.0;
            conv.weights[0][0][1][1] = 1.0;
            conv.weights[0][0][1][2] = 0.0;
            conv.weights[0][0][2][0] = 0.0;
            conv.weights[0][0][2][1] = 0.0;
            conv.weights[0][0][2][2] = 1.0;
            conv.bias[0] = 0.0;
            
            var input = [[[
                [1.0, 2.0, 3.0, 4.0],
                [5.0, 6.0, 7.0, 8.0],
                [9.0, 10.0, 11.0, 12.0],
                [13.0, 14.0, 15.0, 16.0]
            ]]];
            
            var output = conv.forward(input);
            io.print(int(output[0][0][0][0]));
            io.print(int(output[0][0][0][1]));
        ";
        var result = RunCode(code);
        Assert.Contains("12", result.Output);
        Assert.Contains("15", result.Output);
    }
    
    [Fact]
    public void Conv2D_MultiChannel_WorksCorrectly()
    {
        const string code = @"
            var conv = new Conv2D(2, 1, 2, 1, ""valid"", 0.0);
            // Initialize all weights
            for (var ic = 0; ic < 2; ic = ic + 1) {
                for (var oc = 0; oc < 1; oc = oc + 1) {
                    for (var kh = 0; kh < 2; kh = kh + 1) {
                        for (var kw = 0; kw < 2; kw = kw + 1) {
                            conv.weights[ic][oc][kh][kw] = 1.0;
                        }
                    }
                }
            }
            conv.bias[0] = 0.0;
            
            var input = [[[
                [1.0, 2.0],
                [3.0, 4.0]
            ], [
                [5.0, 6.0],
                [7.0, 8.0]
            ]]];
            
            var output = conv.forward(input);
            io.print(int(output[0][0][0][0]));
        ";
        var result = RunCode(code);
        Assert.Contains("36", result.Output); // 1+2+3+4+5+6+7+8
    }
    
    [Fact]
    public void MaxPool2D_Forward_WorksCorrectly()
    {
        const string code = @"
            var pool = new MaxPool2D(2, 2);
            var input = [[[
                [1.0, 2.0, 3.0, 4.0],
                [5.0, 6.0, 7.0, 8.0],
                [9.0, 10.0, 11.0, 12.0],
                [13.0, 14.0, 15.0, 16.0]
            ]]];
            
            var output = pool.forward(input);
            io.print(int(output[0][0][0][0]));
            io.print(int(output[0][0][0][1]));
            io.print(int(output[0][0][1][0]));
            io.print(int(output[0][0][1][1]));
        ";
        var result = RunCode(code);
        Assert.Contains("6", result.Output);
        Assert.Contains("8", result.Output);
        Assert.Contains("14", result.Output);
        Assert.Contains("16", result.Output);
    }
    
    [Fact]
    public void AvgPool2D_Forward_WorksCorrectly()
    {
        const string code = @"
            var pool = new AvgPool2D(2, 2);
            var input = [[[
                [1.0, 2.0, 3.0, 4.0],
                [5.0, 6.0, 7.0, 8.0],
                [9.0, 10.0, 11.0, 12.0],
                [13.0, 14.0, 15.0, 16.0]
            ]]];
            
            var output = pool.forward(input);
            io.print(int(output[0][0][0][0]));
            io.print(int(output[0][0][0][1]));
            io.print(int(output[0][0][1][0]));
            io.print(int(output[0][0][1][1]));
        ";
        var result = RunCode(code);
        Assert.Contains("3", result.Output); // (1+2+5+6)/4
        Assert.Contains("5", result.Output); // (3+4+7+8)/4
        Assert.Contains("11", result.Output); // (9+10+13+14)/4
        Assert.Contains("13", result.Output); // (11+12+15+16)/4
    }
    
    [Fact]
    public void Flatten_Forward_WorksCorrectly()
    {
        const string code = @"
            var flatten = new Flatten();
            var input = [[[
                [1.0, 2.0],
                [3.0, 4.0]
            ], [
                [5.0, 6.0],
                [7.0, 8.0]
            ]]];
            
            var output = flatten.forward(input);
            io.print(output.length);
            io.print(int(output[0]));
            io.print(int(output[7]));
        ";
        var result = RunCode(code);
        Assert.Contains("8", result.Output);
        Assert.Contains("1", result.Output);
        Assert.Contains("8", result.Output);
    }
    
    [Fact]
    public void GlobalAvgPool2D_Forward_WorksCorrectly()
    {
        const string code = @"
            var pool = new GlobalAvgPool2D();
            var input = [[[
                [1.0, 2.0],
                [3.0, 4.0]
            ], [
                [5.0, 6.0],
                [7.0, 8.0]
            ]]];
            
            var output = pool.forward(input);
            io.print(int(output[0]));
            io.print(int(output[1]));
        ";
        var result = RunCode(code);
        Assert.Contains("2", result.Output); // (1+2+3+4)/4
        Assert.Contains("6", result.Output); // (5+6+7+8)/4
    }
    
    [Fact]
    public void GlobalMaxPool2D_Forward_WorksCorrectly()
    {
        const string code = @"
            var pool = new GlobalMaxPool2D();
            var input = [[[
                [1.0, 2.0],
                [3.0, 4.0]
            ], [
                [5.0, 6.0],
                [7.0, 8.0]
            ]]];
            
            var output = pool.forward(input);
            io.print(int(output[0]));
            io.print(int(output[1]));
        ";
        var result = RunCode(code);
        Assert.Contains("4", result.Output);
        Assert.Contains("8", result.Output);
    }
    
    [Fact]
    public void Sequential_WithMixedLayers_WorksCorrectly()
    {
        const string code = @"
            math.seed(42);
            var net = new Sequential([
                new Conv2D(1, 2, 2, 1, ""valid"", 0.1),
                new Flatten(),
                new Dense(8, 2, ""relu"", 0.1)
            ]);
            
            var input = [[[
                [1.0, 2.0, 3.0],
                [4.0, 5.0, 6.0],
                [7.0, 8.0, 9.0]
            ]]];
            
            var output = net.forward(input);
            io.print(output.length);
            io.print(""ok"");
        ";
        var result = RunCode(code);
        Assert.Contains("2", result.Output);
        Assert.Contains("ok", result.Output);
    }
    
    [Fact]
    public void Sequential_NestedNetworks_WorksCorrectly()
    {
        const string code = @"
            math.seed(42);
            var convBlock = new Sequential([
                new Conv2D(1, 2, 2, 1, ""valid"", 0.1),
                new MaxPool2D(2, 2)
            ]);
            
            var classifier = new Sequential([
                new Flatten(),
                new Dense(2, 2, ""relu"", 0.1)
            ]);
            
            var fullNet = new Sequential([convBlock, classifier]);
            
            var input = [[[
                [1.0, 2.0, 3.0, 4.0],
                [5.0, 6.0, 7.0, 8.0],
                [9.0, 10.0, 11.0, 12.0],
                [13.0, 14.0, 15.0, 16.0]
            ]]];
            
            var output = fullNet.forward(input);
            io.print(output.length);
            io.print(""ok"");
        ";
        var result = RunCode(code);
        Assert.Contains("2", result.Output);
        Assert.Contains("ok", result.Output);
    }
    
    [Fact]
    public void BatchNorm2D_Training_Mode_WorksCorrectly()
    {
        const string code = @"
            math.seed(42);
            var bn = new BatchNorm2D(2, 0.1, 0.00001);
            bn.train();
            
            var input = [[[
                [1.0, 2.0],
                [3.0, 4.0]
            ], [
                [5.0, 6.0],
                [7.0, 8.0]
            ]]];
            
            var output = bn.forward(input);
            io.print(""training:"");
            io.print(bn.training);
            io.print(""ok"");
        ";
        var result = RunCode(code);
        Assert.Contains("training:", result.Output);
        Assert.Contains("true", result.Output);
        Assert.Contains("ok", result.Output);
    }
    
    [Fact]
    public void BatchNorm2D_Eval_Mode_WorksCorrectly()
    {
        const string code = @"
            math.seed(42);
            var bn = new BatchNorm2D(2, 0.1, 0.00001);
            bn.eval();
            
            var input = [[[
                [1.0, 2.0],
                [3.0, 4.0]
            ], [
                [5.0, 6.0],
                [7.0, 8.0]
            ]]];
            
            var output = bn.forward(input);
            io.print(""training:"");
            io.print(bn.training);
            io.print(""ok"");
        ";
        var result = RunCode(code);
        Assert.Contains("training:", result.Output);
        Assert.Contains("false", result.Output);
        Assert.Contains("ok", result.Output);
    }
    
    [Fact]
    public void Dropout2D_Training_Mode_DropsValues()
    {
        const string code = @"
            math.seed(42);
            var dropout = new Dropout2D(0.5);
            dropout.train();
            
            var input = [[[
                [1.0, 2.0],
                [3.0, 4.0]
            ]]];
            
            var output = dropout.forward(input);
            io.print(""ok"");
        ";
        var result = RunCode(code);
        Assert.Contains("ok", result.Output);
    }
    
    [Fact]
    public void Conv2D_Backward_ComputesGradients()
    {
        const string code = @"
            math.seed(42);
            var conv = new Conv2D(1, 1, 2, 1, ""valid"", 0.1);
            
            var input = [[[
                [1.0, 2.0, 3.0],
                [4.0, 5.0, 6.0],
                [7.0, 8.0, 9.0]
            ]]];
            
            var output = conv.forward(input);
            var upstream = [[[
                [1.0, 1.0],
                [1.0, 1.0]
            ]]];
            
            var dInput = conv.backward(upstream);
            conv.sgd(0.1);
            io.print(""ok"");
        ";
        var result = RunCode(code);
        Assert.Contains("ok", result.Output);
    }
    
    [Fact]
    public void Sequential_BackwardPropagation_WorksCorrectly()
    {
        const string code = @"
            math.seed(42);
            var net = new Sequential([
                new Conv2D(1, 2, 2, 1, ""valid"", 0.1),
                new Flatten(),
                new Dense(8, 1, ""linear"", 0.1)
            ]);
            
            var input = [[[
                [1.0, 2.0, 3.0],
                [4.0, 5.0, 6.0],
                [7.0, 8.0, 9.0]
            ]]];
            
            var output = net.forward(input);
            var upstream = [1.0];
            net.backward(upstream);
            net.sgd(0.1);
            io.print(""ok"");
        ";
        var result = RunCode(code);
        Assert.Contains("ok", result.Output);
    }
}
