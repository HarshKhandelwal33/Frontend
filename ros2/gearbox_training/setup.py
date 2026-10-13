from setuptools import setup

setup(name='gearbox_training', version='0.1.0', packages=['gearbox_training'],
      data_files=[('share/ament_index/resource_index/packages', ['resource/gearbox_training']),
                  ('share/gearbox_training', ['package.xml'])],
      install_requires=['setuptools'], zip_safe=True,
      maintainer='Gearbox project', maintainer_email='maintainer@example.invalid',
      description='Unity gearbox simulation ROS 2 client', license='UNLICENSED',
      entry_points={'console_scripts': ['control = gearbox_training.control:main']})
