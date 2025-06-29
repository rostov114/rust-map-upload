<?php

define('DOCROOT', getcwd().DIRECTORY_SEPARATOR);

require DOCROOT . 'inc/config.php';
require DOCROOT . 'vendor/autoload.php';

use Aws\S3\S3Client;
use Aws\Exception\AwsException;

class core
{
	public static function isLocalOrZeroIP(string $ip): bool
	{
		if ($ip === '0.0.0.0')
			return true;

		if (!filter_var($ip, FILTER_VALIDATE_IP, FILTER_FLAG_IPV4))
			return false;

		return 
			$ip === '127.0.0.1' ||
			str_starts_with($ip, '10.') ||
			str_starts_with($ip, '192.168.') ||
			preg_match('/^172\.(1[6-9]|2[0-9]|3[0-1])\./', $ip);
	}

	public static function error() : void
	{
		http_response_code(404);
		exit();
	}

	public static function exception(AwsException $e) : void
	{
		http_response_code(500);
		echo "S3 error: " . $e->getMessage();
		exit();
	}

	public static function result(string $url, array $_CONFIG) : void
	{
		http_response_code(200);
		echo ($_CONFIG['custom_domain'] !== FALSE) ? str_replace($_CONFIG['endpoint'] . '/' . $_CONFIG['bucket'], $_CONFIG['custom_domain'], $url) : $url;
		exit();
	}
}

if ($_SERVER['REQUEST_METHOD'] == 'PUT' AND isset($_SERVER['HTTP_X_SERVER_IP']) AND isset($_SERVER['HTTP_X_SERVER_PORT']) AND isset($_SERVER['HTTP_X_SERVER_ID']))
{
	$mapData = file_get_contents('php://input');
	if (strlen($mapData) == 0)
		core::error();

	$serverIp = core::isLocalOrZeroIP($_SERVER['HTTP_X_SERVER_IP']) ? $_SERVER['REMOTE_ADDR'] : $_SERVER['HTTP_X_SERVER_IP'];
	$serverPort = $_SERVER['HTTP_X_SERVER_PORT'];
	$serverAppId = $_SERVER['HTTP_X_SERVER_ID'];
	$serverId = ($serverAppId != FALSE) ? $serverAppId : sha1($serverIp . '.' . $serverPort);

	$mapHash = hash('sha256', $mapData);
	$mapFilename = preg_replace(
		'/proceduralmap\.(\d+)\.(\d+)\.(\d+)\.map$/',
		'proceduralmap.$1.$2.$3_' . substr($mapHash, 0, 32) . '.map',
		basename($_SERVER['REQUEST_URI'])
	);

	$currentMapUrl = $_CONFIG['prefix'] . '/' . $serverId . '/' . $mapFilename;

	$s3 = new S3Client(
	[
		'version' => 'latest',
		'region' => $_CONFIG['region'],
		'endpoint' => $_CONFIG['endpoint'],
		'credentials' => 
		[
			'key' => $_CONFIG['accessKey'],
			'secret' => $_CONFIG['secretKey'],
		],
		'use_path_style_endpoint' => true,
	]);

	try
	{
		$exists = $s3->doesObjectExist($_CONFIG['bucket'], $currentMapUrl);
		if ($exists)
			core::result($s3->getObjectUrl($_CONFIG['bucket'], $currentMapUrl), $_CONFIG);

		$oldMaps = $s3->listObjectsV2(
		[
			'Bucket' => $_CONFIG['bucket'],
			'Prefix' => $_CONFIG['prefix'] . '/' . $serverId,
		]);

		if (!empty($oldMaps['Contents']))
		{
			foreach ($oldMaps['Contents'] as $object)
			{
				$s3->deleteObject(
				[
					'Bucket' => $_CONFIG['bucket'],
					'Key' => $object['Key'],
				]);
			}
		}

		$result = $s3->putObject([
			'Bucket' => $_CONFIG['bucket'],
			'Key'    => $currentMapUrl,
			'Body'   => $mapData,
			'ACL'    => 'public-read'
		]);

		core::result($result['ObjectURL'], $_CONFIG);
	}
	catch (AwsException $e) 
	{
		core::exception($e);
	}
}
core::error();