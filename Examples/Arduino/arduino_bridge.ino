// Arduino Bridge Sketch for MALDA
// This sketch enables Arduino to communicate with MALDA via serial port
// Upload this sketch to your Arduino board

String inputString = "";
boolean stringComplete = false;

void setup() {
  Serial.begin(9600);
  inputString.reserve(200);
  
  // Wait for serial connection
  while (!Serial) {
    ; // Wait for serial port to connect
  }
  
  Serial.println("READY");
}

void loop() {
  if (stringComplete) {
    processCommand(inputString);
    inputString = "";
    stringComplete = false;
  }
}

void serialEvent() {
  while (Serial.available()) {
    char inChar = (char)Serial.read();
    inputString += inChar;
    if (inChar == '\n') {
      stringComplete = true;
    }
  }
}

void processCommand(String cmd) {
  cmd.trim();
  
  if (cmd.startsWith("DIGITAL_WRITE:")) {
    int colon1 = cmd.indexOf(':');
    int colon2 = cmd.indexOf(':', colon1 + 1);
    if (colon1 != -1 && colon2 != -1) {
      int pin = cmd.substring(colon1 + 1, colon2).toInt();
      int value = cmd.substring(colon2 + 1).toInt();
      digitalWrite(pin, value ? HIGH : LOW);
      Serial.println("OK");
    } else {
      Serial.println("ERROR:Invalid format");
    }
  }
  else if (cmd.startsWith("DIGITAL_READ:")) {
    int colon = cmd.indexOf(':');
    if (colon != -1) {
      int pin = cmd.substring(colon + 1).toInt();
      pinMode(pin, INPUT);
      int value = digitalRead(pin);
      Serial.print("OK:");
      Serial.println(value);
    } else {
      Serial.println("ERROR:Invalid format");
    }
  }
  else if (cmd.startsWith("ANALOG_READ:")) {
    int colon = cmd.indexOf(':');
    if (colon != -1) {
      int pin = cmd.substring(colon + 1).toInt();
      int value = analogRead(pin);
      Serial.print("OK:");
      Serial.println(value);
    } else {
      Serial.println("ERROR:Invalid format");
    }
  }
  else if (cmd.startsWith("ANALOG_WRITE:")) {
    int colon1 = cmd.indexOf(':');
    int colon2 = cmd.indexOf(':', colon1 + 1);
    if (colon1 != -1 && colon2 != -1) {
      int pin = cmd.substring(colon1 + 1, colon2).toInt();
      int value = cmd.substring(colon2 + 1).toInt();
      analogWrite(pin, value);
      Serial.println("OK");
    } else {
      Serial.println("ERROR:Invalid format");
    }
  }
  else if (cmd.startsWith("PIN_MODE:")) {
    int colon1 = cmd.indexOf(':');
    int colon2 = cmd.indexOf(':', colon1 + 1);
    if (colon1 != -1 && colon2 != -1) {
      int pin = cmd.substring(colon1 + 1, colon2).toInt();
      String mode = cmd.substring(colon2 + 1);
      mode.toUpperCase();
      if (mode == "INPUT") {
        pinMode(pin, INPUT);
      } else if (mode == "OUTPUT") {
        pinMode(pin, OUTPUT);
      } else if (mode == "INPUT_PULLUP") {
        pinMode(pin, INPUT_PULLUP);
      } else {
        Serial.println("ERROR:Invalid mode");
        return;
      }
      Serial.println("OK");
    } else {
      Serial.println("ERROR:Invalid format");
    }
  }
  else {
    Serial.print("ERROR:Unknown command:");
    Serial.println(cmd);
  }
}
