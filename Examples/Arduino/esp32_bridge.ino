// ESP32 Bridge Sketch for MALDA
// This sketch enables ESP32 to communicate with MALDA via HTTP/REST API
// Upload this sketch to your ESP32 board

#include <WiFi.h>
#include <WebServer.h>
#include <ArduinoJson.h>

// WiFi credentials - CHANGE THESE
const char* ssid = "YourWiFiSSID";
const char* password = "YourWiFiPassword";

WebServer server(80);

void setup() {
  Serial.begin(115200);
  
  // Connect to WiFi
  WiFi.begin(ssid, password);
  Serial.print("Connecting to WiFi");
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  Serial.println("");
  Serial.println("WiFi connected!");
  Serial.print("IP address: ");
  Serial.println(WiFi.localIP());
  
  // REST API endpoints
  server.on("/ping", handlePing);
  server.on("/digital/write", HTTP_POST, handleDigitalWrite);
  server.on("/digital/read", HTTP_GET, handleDigitalRead);
  server.on("/analog/read", HTTP_GET, handleAnalogRead);
  server.on("/analog/write", HTTP_POST, handleAnalogWrite);
  server.on("/pin/mode", HTTP_POST, handlePinMode);
  
  server.begin();
  Serial.println("HTTP server started");
}

void loop() {
  server.handleClient();
}

void handlePing() {
  server.send(200, "application/json", "{\"status\":\"ok\"}");
}

void handleDigitalWrite() {
  if (server.hasArg("plain")) {
    String body = server.arg("plain");
    
    StaticJsonDocument<200> doc;
    DeserializationError error = deserializeJson(doc, body);
    
    if (error) {
      server.send(400, "application/json", "{\"error\":\"Invalid JSON\"}");
      return;
    }
    
    int pin = doc["pin"];
    int value = doc["value"];
    
    digitalWrite(pin, value ? HIGH : LOW);
    server.send(200, "application/json", "{\"success\":true}");
  } else {
    server.send(400, "application/json", "{\"error\":\"No body\"}");
  }
}

void handleDigitalRead() {
  if (server.hasArg("pin")) {
    int pin = server.arg("pin").toInt();
    pinMode(pin, INPUT);
    int value = digitalRead(pin);
    
    String json = "{\"value\":";
    json += value;
    json += "}";
    server.send(200, "application/json", json);
  } else {
    server.send(400, "application/json", "{\"error\":\"Missing pin parameter\"}");
  }
}

void handleAnalogRead() {
  if (server.hasArg("pin")) {
    int pin = server.arg("pin").toInt();
    int value = analogRead(pin);
    
    String json = "{\"value\":";
    json += value;
    json += "}";
    server.send(200, "application/json", json);
  } else {
    server.send(400, "application/json", "{\"error\":\"Missing pin parameter\"}");
  }
}

void handleAnalogWrite() {
  if (server.hasArg("plain")) {
    String body = server.arg("plain");
    
    StaticJsonDocument<200> doc;
    DeserializationError error = deserializeJson(doc, body);
    
    if (error) {
      server.send(400, "application/json", "{\"error\":\"Invalid JSON\"}");
      return;
    }
    
    int pin = doc["pin"];
    int value = doc["value"];
    
    analogWrite(pin, value);
    server.send(200, "application/json", "{\"success\":true}");
  } else {
    server.send(400, "application/json", "{\"error\":\"No body\"}");
  }
}

void handlePinMode() {
  if (server.hasArg("plain")) {
    String body = server.arg("plain");
    
    StaticJsonDocument<200> doc;
    DeserializationError error = deserializeJson(doc, body);
    
    if (error) {
      server.send(400, "application/json", "{\"error\":\"Invalid JSON\"}");
      return;
    }
    
    int pin = doc["pin"];
    String mode = doc["mode"];
    mode.toUpperCase();
    
    if (mode == "INPUT") {
      pinMode(pin, INPUT);
    } else if (mode == "OUTPUT") {
      pinMode(pin, OUTPUT);
    } else if (mode == "INPUT_PULLUP") {
      pinMode(pin, INPUT_PULLUP);
    } else {
      server.send(400, "application/json", "{\"error\":\"Invalid mode\"}");
      return;
    }
    
    server.send(200, "application/json", "{\"success\":true}");
  } else {
    server.send(400, "application/json", "{\"error\":\"No body\"}");
  }
}
