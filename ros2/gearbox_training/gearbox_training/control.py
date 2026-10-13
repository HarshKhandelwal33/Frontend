"""Send one simulation command and wait for a matching dispatch acknowledgement."""
import argparse
import json
import time
from uuid import uuid4


def main(args=None):
    import rclpy
    from rclpy.node import Node
    from std_msgs.msg import String

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['ready', 'pause', 'resume', 'reset'])
    options = parser.parse_args(args)
    rclpy.init(args=[])
    node = Node('gearbox_simulation_control')
    request_id = str(uuid4())
    publisher = node.create_publisher(String, '/gearbox/simulation/control', 10)
    replies = []

    def on_reply(message):
        try:
            reply = json.loads(message.data)
            if reply.get('request_id') == request_id:
                replies.append(reply)
        except (ValueError, TypeError):
            pass

    subscription = node.create_subscription(String, '/gearbox/simulation/control_ack', on_reply, 10)
    deadline = time.monotonic() + 10
    sent = False
    try:
        while time.monotonic() < deadline and not replies:
            rclpy.spin_once(node, timeout_sec=0.1)
            if not sent and publisher.get_subscription_count() > 0:
                publisher.publish(String(data=json.dumps(dict(request_id=request_id, action=options.action))))
                sent = True
        if not replies:
            raise SystemExit('No acknowledgement. Check Unity Play Mode, rosbridge, and topic connectivity. Motion was not retried.')
        print(json.dumps(replies[0], indent=2))
        if replies[0].get('status') != 'dispatched':
            raise SystemExit(1)
    finally:
        node.destroy_subscription(subscription)
        node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()
