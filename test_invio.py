import paho.mqtt.client as client

BROKER = "192.168.1.10"
TOPIC = "prova"

def on_connect(subscriber, userdata, flags, rc):
    print(f"Connesso con return code {str(rc)}")
    subscriber.subscribe(TOPIC)

def on_message(subscriber, userdata, msg):
    dato = msg.payload.decode()
    print(f"{msg.topic}: {dato}")

subscriber = client.Client()
subscriber.on_connect = on_connect
subscriber.on_message = on_message

subscriber.connect(BROKER, 1883)

subscriber.loop_forever()